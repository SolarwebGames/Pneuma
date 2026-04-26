using SolarWeb.Pneuma.Simulation.Data;
using SolarWeb.Pneuma.Atoms;
using SolarWeb.Pneuma.Gas;
using SolarWeb.Pneuma.GasExchange;
using SolarWeb.Pneuma.Grid;
using SolarWeb.Pneuma.Metabolism;
using SolarWeb.Pneuma.Data;
using SolarWeb.Pneuma.Logging;
using System;
using System.Collections.Generic;
using Unity.Jobs;
using Unity.Collections;

namespace SolarWeb.Pneuma.Simulation
{
	public class AtmosphereManager : IDisposable
	{
		public readonly ExchangerBuffer ExchangerBuffer = new();
		public AtmosphereGrid State;
		public MetabolismState Metabolism = null!;
		public readonly AtomicRegistry AtomicRegistry = new();
		public readonly GasRegistry GasRegistry = new();
		public AtmosphereJobLogger Logger { get; private set; }
		public bool IsLoggingSessionActive => Logger.IsSessionActive;
		public int SimulationTick { get; private set; }
		public int LoggingRemainingTicks { get; private set; }
		private int ticksSinceBiologicalDangerUpdate = 0;

		public Unity.Mathematics.float2 WindVelocity;
		public NativeList<int> DirtyFaceIndices;

		/// <summary>
		/// Starts a logging session that will automatically stop after duration ticks.
		/// </summary>
		public void StartLoggingSession(int targetIdx, string targetName, int duration, int sampleInterval, params JobLogId[] jobs)
		{
			Logger.StartLogging(targetIdx, targetName, sampleInterval, jobs);
			LoggingRemainingTicks = duration;
		}

		/// <summary>
		/// When true, every simulation step completes its job immediately before the next is scheduled.
		/// Useful for per-job performance profiling. Toggle off for production.
		/// </summary>
		public static bool SequentialMode = true;

		// Specialized Simulators
		public AtmosphereMaintenance Maintenance { get; private set; } = null!;
		public RegionPhysics RegionPhysics { get; private set; } = null!;
		public RegionDiffusion RegionDiffusion { get; private set; } = null!;
		public DynamicDiffusion DynamicDiffusion { get; private set; } = null!;
		public RegionReactions RegionReactions { get; private set; } = null!;
		public RegionBreach RegionBreach { get; private set; } = null!;
		public RegionOverpressure Overpressure { get; private set; } = null!;
		public RegionGraphMutator RegionGraphMutator { get; private set; } = null!;
		public DangerScanner DangerScanner { get; private set; } = null!;
		public RespiratorySimulator RespiratorySimulator { get; private set; } = null!;
		public PlasmaSimulator PlasmaSimulator { get; private set; } = null!;
		public MetabolismLifecycle MetabolismLifecycle { get; private set; } = null!;
		public ExchangerSimulator ExchangerSimulator { get; private set; } = null!;
		public SimplifiedMetabolismSimulator SimplifiedMetabolism { get; private set; } = null!;
		public PlantAtmosphereSimulator PlantSimulator { get; private set; } = null!;
		public AtmosphereStateExporter StateExporter { get; private set; } = null!;
		public SolarWeb.Pneuma.Simulation.Environment.WindMapManager WindMap { get; private set; } = null!;
		public SolarWeb.Pneuma.Simulation.UI.AtmosphereVisualsHandler Visuals { get; private set; } = null!;

		public AtmosphereManager(AtmosphereGrid state)
		{
			State = state;
			Logger = new AtmosphereJobLogger();
			DirtyFaceIndices = new Unity.Collections.NativeList<int>(64, Unity.Collections.Allocator.Persistent);
			InitializeSimulators();
			Visuals = new SolarWeb.Pneuma.Simulation.UI.AtmosphereVisualsHandler(this);
		}

		private void InitializeSimulators()
		{
			Maintenance = new AtmosphereMaintenance(this);
			RegionPhysics = new RegionPhysics(this);
			RegionDiffusion = new RegionDiffusion(this);
			DynamicDiffusion = new DynamicDiffusion(this);
			RegionReactions = new RegionReactions(this);
			RegionBreach = new RegionBreach(this);
			Overpressure = new RegionOverpressure(this);
			RegionGraphMutator = new RegionGraphMutator(this);
			DangerScanner = new DangerScanner(this);
			RespiratorySimulator = new RespiratorySimulator(this);
			PlasmaSimulator = new PlasmaSimulator(this);
			MetabolismLifecycle = new MetabolismLifecycle(this);
			ExchangerSimulator = new ExchangerSimulator(this);
			SimplifiedMetabolism = new SimplifiedMetabolismSimulator(this);
			PlantSimulator = new PlantAtmosphereSimulator(this);
			StateExporter = new AtmosphereStateExporter(this);
			WindMap = new SolarWeb.Pneuma.Simulation.Environment.WindMapManager(this);
			WindMap.Initialize();
		}

		public void Initialize(List<AtomDefinition> atoms, List<GasDefinition> gases)
		{
			AtomicRegistry.Initialize(atoms);
			var stoichiometry = new GasStoichiometry();
			stoichiometry.Initialize(gases.Count, AtomicRegistry.TotalAtomCount);
			GasRegistry.Initialize(gases, AtomicRegistry, stoichiometry);
			Metabolism = new(ExchangerBuffer, GasRegistry, AtomicRegistry);
			Metabolism.InitializeDangerBuffers(State.RegionStride, State.Stride);
		}

		/// <summary>
		/// Writes ambient-environment values into the sentinel region slot so compute jobs
		/// can read it like any other region — no per-job branching for sentinel endpoints.
		/// Must be called on the main thread before scheduling diffusion jobs each tick.
		/// </summary>
		private void SyncSentinelPhysics()
		{
			var st = State;
			int sent = st.SentinelRegionIndex;
			var amb = st.AmbientEnvironment;

			st.RegionGasComposition.PressureKpa[sent] = amb.TotalPressureKpa;
			st.RegionPhysicsBuffer.TemperatureK[sent] = amb.TemperatureK;
			long ambTotal = amb.TotalUMolesInOneCell;
			st.RegionGasComposition.TotalUMoles[sent] = ambTotal;

			float T = amb.TemperatureK;
			float P = Unity.Mathematics.math.max(amb.TotalPressureKpa, 1e-3f);
			st.RegionPhysicsBuffer.SpeedOfSound[sent] = Unity.Mathematics.math.sqrt(1.4f * 8314f * T / 29f);
			st.RegionPhysicsBuffer.TFactor[sent] = T / P;
			float nMol = ambTotal / 1_000_000f;
			st.RegionPhysicsBuffer.InvVolConst[sent] = (nMol > 1e-9f) ? 1f / (nMol * 8.31446f) : 0f;

			for (int g = 0; g < st.GasCount; g++)
			{
				long ambUMol = amb.uMolesInOneCell.IsCreated ? amb.uMolesInOneCell[g] : 0L;
				st.RegionGasComposition.uMoles[st.GetRegionUMoleIndex(g, sent)] = ambUMol;
				st.RegionPhysicsBuffer.MolarFractions[g * st.RegionStride + sent] =
					ambTotal > 0 ? (float)ambUMol / ambTotal : 0f;
			}
		}

		public void Tick(float timeStep, int elapsedTicks = 1)
		{
			if (DirtyFaceIndices.Length > 0)
			{
				new Jobs.Diffusion.RecomputeDirtyFaceMetrics
				{
					DirtyFaceIndices = DirtyFaceIndices.AsArray(),
					CellThermalConductivity = State.TopologyBuffer.CellThermalConductivity,
					CellGasPermeability = State.TopologyBuffer.CellGasPermeability,
					CellMaxPressureDeltaKpa = State.TopologyBuffer.CellMaxPressureDeltaKpa,
					CellMinColDia = State.TopologyBuffer.CellMinColDia,
					CellMaxColDia = State.TopologyBuffer.CellMaxColDia,
					CellPumpRate = State.TopologyBuffer.CellPumpRate,
					CellFlowDirX = State.TopologyBuffer.CellFlowDirX,
					CellFlowDirZ = State.TopologyBuffer.CellFlowDirZ,
					CellFlags = State.TopologyBuffer.CellFlags,

					CellTopPermeability = State.TopologyBuffer.CellTopPermeability,
					CellTopConductivity = State.TopologyBuffer.CellTopConductivity,
					CellBottomPermeability = State.TopologyBuffer.CellBottomPermeability,
					CellBottomConductivity = State.TopologyBuffer.CellBottomConductivity,

					WorldToRegionIndex = State.GridLookups.WorldToRegionIndex,
					SimToWorldIndex = State.GridLookups.SimToWorldIndex,
					FaceLinkOffsets = State.TopologyBuffer.FaceLinkOffsets,
					FaceLinkCounts = State.TopologyBuffer.FaceLinkCounts,
					FaceLinkWorldStart = State.TopologyBuffer.FaceLinkWorldStart,
					FaceLinkDirection = State.TopologyBuffer.FaceLinkDirection,

					FaceType = State.RegionFaceBuffer.FaceType,
					RegionFaceToCellOffsets = State.RegionFaceBuffer.RegionFaceToCellOffsets,
					RegionFaceToCellCounts = State.RegionFaceBuffer.RegionFaceToCellCounts,
					RegionFaceToCellSimA = State.RegionFaceBuffer.RegionFaceToCellSimA,

					MapWidth = State.MapWidth,
					MapHeight = State.MapHeight,
					SentinelRegionIndex = State.SentinelRegionIndex,
					FaceGasPermeability = State.RegionFaceBuffer.FaceGasPermeability,
					FaceThermalConductivity = State.RegionFaceBuffer.FaceThermalConductivity,
					FaceMaxPressureDeltaKpa = State.RegionFaceBuffer.FaceMaxPressureDeltaKpa,
					MinCollisionDiameter = State.RegionFaceBuffer.MinCollisionDiameter,
					MaxCollisionDiameter = State.RegionFaceBuffer.MaxCollisionDiameter,
					FaceActivePumpRate = State.RegionFaceBuffer.FaceActivePumpRate,
					FaceFlowDirection = State.RegionFaceBuffer.FaceFlowDirection
				}.Schedule(DirtyFaceIndices.Length, 8).Complete();
				DirtyFaceIndices.Clear();
			}

			SimulationTick += elapsedTicks;
			if (LoggingRemainingTicks > 0)
			{
				LoggingRemainingTicks -= elapsedTicks;
				if (LoggingRemainingTicks <= 0)
				{
					LoggingRemainingTicks = 0;
					Logger.EndSession();
				}
			}

			// Zero out debug flux buffer
			State.RegionGasComposition.RegionNetFlux.Clear();

			// Populate sentinel physics slot so diffusion jobs can treat it as a boundary condition.
			SyncSentinelPhysics();

			// Main-thread: materialise any pending cell splits before jobs begin.
			// Must run before any jobs that read WorldToRegionIndex or DynFace lists.
			RegionGraphMutator.ProcessQueue();

			JobHandle handle = default;

			// Update wind exposure map if needed.
			handle = WindMap.UpdateWindExposure(handle);
			handle = Complete_WindExposure(handle);

			// === Phase 1: Region Physics Sync ===
			handle = RegionPhysics.DoSync(handle);
			handle = Complete_RegionSync(handle);
			handle = RegionPhysics.DoGatherActiveRegions(handle);
			handle = Complete_GatherActiveRegions(handle);

			// Precompute static and dynamic faces. In sequential mode these run one after the other;
			// in parallel mode they overlap (both depend on handle, which is not yet completed).
			var staticFaceHandle = RegionPhysics.DoPrecomputeFacePhysics(timeStep, handle);
			staticFaceHandle = Complete_PrecomputeStaticFaces(staticFaceHandle);
			var dynamicFaceHandle = RegionPhysics.DoPrecomputeDynamicFacePhysics(timeStep, handle);
			dynamicFaceHandle = Complete_PrecomputeDynamicFaces(dynamicFaceHandle);

			// === Phase 2: Parallel Simulation Pipelines ===

			// Branch A: Danger Scanning
			ticksSinceBiologicalDangerUpdate++;
			bool updateBiological = ticksSinceBiologicalDangerUpdate >= 4;
			if (updateBiological) ticksSinceBiologicalDangerUpdate = 0;

			var dangerHandle = DangerScanner.DoComputeDanger(handle, updateBiological);
			dangerHandle = Complete_DangerScan(dangerHandle);

			// Branch B: Region + Dynamic Diffusion (after both precompute jobs)
			var precomputeDone = JobHandle.CombineDependencies(staticFaceHandle, dynamicFaceHandle);

			var regionGasHandle = RegionDiffusion.DoGasDiffusion(timeStep, precomputeDone);
			regionGasHandle = Complete_RegionGasDiffusion(regionGasHandle);
			var dynamicGasHandle = DynamicDiffusion.DoGasDiffusion(timeStep, precomputeDone);
			dynamicGasHandle = Complete_DynamicGasDiffusion(dynamicGasHandle);

			// Sum total flux for liquid entrainment
			var sumRegionHandle = RegionDiffusion.DoSumTotalFlux(regionGasHandle);
			sumRegionHandle = Complete_SumRegionFlux(sumRegionHandle);
			var sumDynamicHandle = DynamicDiffusion.DoSumTotalFlux(dynamicGasHandle);
			sumDynamicHandle = Complete_SumDynamicFlux(sumDynamicHandle);

			// --- CONSERVATIVE GAS FLUX PIPELINE ---
			// 1. Clear accumulation buffers
			var clearHandle = RegionDiffusion.DoClearDeltas(
					JobHandle.CombineDependencies(sumRegionHandle, sumDynamicHandle));
			clearHandle = Complete_ApplyRegionGasFlux(clearHandle); // Reusing completion method name for simplicity

			// 2. Accumulate static faces
			var accumulateStaticHandle = RegionDiffusion.DoAccumulateFlux(clearHandle);
			accumulateStaticHandle = Complete_ApplyRegionGasFlux(accumulateStaticHandle);

			// 3. Accumulate dynamic faces
			var accumulateDynamicHandle = DynamicDiffusion.DoAccumulateFlux(accumulateStaticHandle);
			accumulateDynamicHandle = Complete_ApplyDynamicGasFlux(accumulateDynamicHandle);

			// 4. Apply all accumulated deltas to regions
			var finalApplyHandle = RegionDiffusion.DoApplyConservativeFlux(accumulateDynamicHandle);
			finalApplyHandle = Complete_ApplyDynamicGasFlux(finalApplyHandle);

			// Conductive thermal flux — dynamic must run after static (same TemperatureK array).
			var regionThermalHandle = RegionDiffusion.DoThermalFlux(timeStep, finalApplyHandle);
			regionThermalHandle = Complete_RegionConductiveThermal(regionThermalHandle);
			var dynamicThermalHandle = DynamicDiffusion.DoThermalFlux(timeStep, regionThermalHandle);
			dynamicThermalHandle = Complete_DynamicConductiveThermal(dynamicThermalHandle);

			// Advective thermal: applied only to the receiving region using the pre-tick
			// PreviousTemperatureK snapshot (set by SyncRegionTotalMoles in Phase 1).
			// Static advective first; dynamic advective second because it can write to static slots.
			var conductionDone = JobHandle.CombineDependencies(regionThermalHandle, dynamicThermalHandle);
			var regionAdvectiveHandle = RegionDiffusion.DoAdvectiveThermal(timeStep, conductionDone);
			regionAdvectiveHandle = Complete_RegionAdvectiveThermal(regionAdvectiveHandle);
			var dynamicAdvectiveHandle = DynamicDiffusion.DoAdvectiveThermal(timeStep, regionAdvectiveHandle);
			dynamicAdvectiveHandle = Complete_DynamicAdvectiveThermal(dynamicAdvectiveHandle);

			var thermalDone = RegionDiffusion.DoStructuralThermalPasses(timeStep, dynamicAdvectiveHandle);
			thermalDone = Complete_StructuralThermal(thermalDone);

			// Reactions depend on fully updated region state
			var regionHandle = RegionReactions.DoProcessCombustion(timeStep, thermalDone);
			regionHandle = Complete_Combustion(regionHandle);
			regionHandle = RegionReactions.DoPhaseTransitions(timeStep, regionHandle);
			regionHandle = Complete_PhaseTransitions(regionHandle);
			regionHandle = RegionReactions.DoSolidPhaseTransitions(timeStep, regionHandle);
			regionHandle = Complete_SolidPhaseTransitions(regionHandle);
			regionHandle = RegionReactions.DoDetectIgnition(regionHandle);
			regionHandle = Complete_DetectIgnition(regionHandle);
			regionHandle = Overpressure.DoDetectOverpressure(regionHandle);
			regionHandle = Complete_DetectOverpressure(regionHandle);

			// Detect expanding pressure fronts, then equilibrated dynamic regions
			var bloomHandle = DynamicDiffusion.DoDetectBloom(thermalDone);
			bloomHandle = Complete_DetectBloom(bloomHandle);
			var equilibriumHandle = DynamicDiffusion.DoDetectEquilibrium(bloomHandle);
			equilibriumHandle = Complete_DetectEquilibrium(equilibriumHandle);

			// Combine all simulation branches before final systems
			var combinedHandle = JobHandle.CombineDependencies(regionHandle, dangerHandle, equilibriumHandle);

			// === Phase 3: Final Systems ===
			// Simplified metabolism and plants run first so they stage gas exchange requests
			// before the exchanger processes them this tick.
			var simplifiedHandle = SimplifiedMetabolism.DoUpdate(timeStep, combinedHandle);
			simplifiedHandle = Complete_SimplifiedMetabolism(simplifiedHandle);
			var plantHandle = PlantSimulator.DoUpdate(timeStep, combinedHandle);
			plantHandle = Complete_PlantSimulator(plantHandle);
			var preExchangerHandle = JobHandle.CombineDependencies(simplifiedHandle, plantHandle);

			var simplifiedExchangerHandle = ExchangerSimulator.DoGasExchanges(timeStep, preExchangerHandle);
			simplifiedExchangerHandle = Complete_GasExchanges(simplifiedExchangerHandle);

			var roomTempDeltaHandle = Maintenance.DoCalculateRoomTemperatureDeltas(simplifiedExchangerHandle);
			roomTempDeltaHandle = Complete_RoomTemperatureDeltas(roomTempDeltaHandle);

			var roomAggregateHandle = Maintenance.DoAggregateRoomData(roomTempDeltaHandle);
			roomAggregateHandle = Complete_AggregateRoomData(roomAggregateHandle);

			var visualsHandle = Visuals.ScheduleVisualsJobs(roomAggregateHandle);
			visualsHandle = Complete_Visuals(visualsHandle);

			var metabolismHandle = RespiratorySimulator.DoBreathe(timeStep, simplifiedExchangerHandle);
			metabolismHandle = Complete_Breathe(metabolismHandle);
			metabolismHandle = RespiratorySimulator.DoPrecompute(metabolismHandle);
			metabolismHandle = Complete_MetabolismPrecompute(metabolismHandle);
			metabolismHandle = PlasmaSimulator.DoPlasmaDiffusion(timeStep, metabolismHandle);
			metabolismHandle = Complete_PlasmaDiffusion(metabolismHandle);
			metabolismHandle = PlasmaSimulator.DoMetabolicReactions(timeStep, metabolismHandle);
			metabolismHandle = Complete_MetabolicReactions(metabolismHandle);
			metabolismHandle = PlasmaSimulator.DoPlasmaFiltration(timeStep, metabolismHandle);
			metabolismHandle = Complete_PlasmaFiltration(metabolismHandle);

			// Final synchronization — explicitly combine all branches so that every job
			// (including deferred ActiveIndices consumers) is guaranteed complete before
			// the next frame's main-thread ActiveIndices.Clear() runs.
			var allSimHandles = JobHandle.CombineDependencies(thermalDone, equilibriumHandle);
			allSimHandles = JobHandle.CombineDependencies(allSimHandles, dangerHandle, visualsHandle);
			var combinedFinalHandle = JobHandle.CombineDependencies(metabolismHandle, roomAggregateHandle, allSimHandles);
			combinedFinalHandle.Complete();

			Visuals.FinalizeVisuals();

			// Main-thread: merge equilibrated dynamic regions back into their parents.
			RegionGraphMutator.AbsorbEquilibrated();

			RespiratorySimulator.DoExternalProviderPostStep();

			MetabolismLifecycle.DoCheckMetabolismThresholds();
			MetabolismLifecycle.DoMetabolismCallbacks();
		}

		public static JobHandle CompleteIfNeeded(JobHandle handle)
		{
			if (SequentialMode) handle.Complete();
			return handle;
		}

		// --- Per-job completion methods ---
		// Each has a unique name so sequential-mode profiling shows a distinct stack frame per job.
		private static JobHandle Complete_WindExposure(JobHandle h) { if (SequentialMode) h.Complete(); return h; }
		private static JobHandle Complete_RegionSync(JobHandle h) { if (SequentialMode) h.Complete(); return h; }
		private static JobHandle Complete_GatherActiveRegions(JobHandle h) { if (SequentialMode) h.Complete(); return h; }
		private static JobHandle Complete_PrecomputeStaticFaces(JobHandle h) { if (SequentialMode) h.Complete(); return h; }
		private static JobHandle Complete_PrecomputeDynamicFaces(JobHandle h) { if (SequentialMode) h.Complete(); return h; }
		private static JobHandle Complete_DangerScan(JobHandle h) { if (SequentialMode) h.Complete(); return h; }
		private static JobHandle Complete_RegionGasDiffusion(JobHandle h) { if (SequentialMode) h.Complete(); return h; }
		private static JobHandle Complete_DynamicGasDiffusion(JobHandle h) { if (SequentialMode) h.Complete(); return h; }
		private static JobHandle Complete_SumRegionFlux(JobHandle h) { if (SequentialMode) h.Complete(); return h; }
		private static JobHandle Complete_SumDynamicFlux(JobHandle h) { if (SequentialMode) h.Complete(); return h; }
		private static JobHandle Complete_ApplyRegionGasFlux(JobHandle h) { if (SequentialMode) h.Complete(); return h; }
		private static JobHandle Complete_ApplyDynamicGasFlux(JobHandle h) { if (SequentialMode) h.Complete(); return h; }
		private static JobHandle Complete_RegionConductiveThermal(JobHandle h) { if (SequentialMode) h.Complete(); return h; }
		private static JobHandle Complete_DynamicConductiveThermal(JobHandle h) { if (SequentialMode) h.Complete(); return h; }
		private static JobHandle Complete_RegionAdvectiveThermal(JobHandle h) { if (SequentialMode) h.Complete(); return h; }
		private static JobHandle Complete_DynamicAdvectiveThermal(JobHandle h) { if (SequentialMode) h.Complete(); return h; }
		private static JobHandle Complete_StructuralThermal(JobHandle h) { if (SequentialMode) h.Complete(); return h; }
		private static JobHandle Complete_Combustion(JobHandle h) { if (SequentialMode) h.Complete(); return h; }
		private static JobHandle Complete_PhaseTransitions(JobHandle h) { if (SequentialMode) h.Complete(); return h; }
		private static JobHandle Complete_SolidPhaseTransitions(JobHandle h) { if (SequentialMode) h.Complete(); return h; }
		private static JobHandle Complete_DetectIgnition(JobHandle h) { if (SequentialMode) h.Complete(); return h; }
		private static JobHandle Complete_DetectOverpressure(JobHandle h) { if (SequentialMode) h.Complete(); return h; }
		private static JobHandle Complete_DetectBloom(JobHandle h) { if (SequentialMode) h.Complete(); return h; }
		private static JobHandle Complete_DetectEquilibrium(JobHandle h) { if (SequentialMode) h.Complete(); return h; }
		private static JobHandle Complete_SimplifiedMetabolism(JobHandle h) { if (SequentialMode) h.Complete(); return h; }
		private static JobHandle Complete_PlantSimulator(JobHandle h) { if (SequentialMode) h.Complete(); return h; }
		private static JobHandle Complete_GasExchanges(JobHandle h) { if (SequentialMode) h.Complete(); return h; }
		private static JobHandle Complete_RoomTemperatureDeltas(JobHandle h) { if (SequentialMode) h.Complete(); return h; }
		private static JobHandle Complete_AggregateRoomData(JobHandle h) { if (SequentialMode) h.Complete(); return h; }
		private static JobHandle Complete_Visuals(JobHandle h) { if (SequentialMode) h.Complete(); return h; }
		private static JobHandle Complete_Breathe(JobHandle h) { if (SequentialMode) h.Complete(); return h; }
		private static JobHandle Complete_MetabolismPrecompute(JobHandle h) { if (SequentialMode) h.Complete(); return h; }
		private static JobHandle Complete_PlasmaDiffusion(JobHandle h) { if (SequentialMode) h.Complete(); return h; }
		private static JobHandle Complete_MetabolicReactions(JobHandle h) { if (SequentialMode) h.Complete(); return h; }
		private static JobHandle Complete_PlasmaFiltration(JobHandle h) { if (SequentialMode) h.Complete(); return h; }

		/// <summary>Queue a cell split into a dynamic region (called by TopologyMapper / breach handlers).</summary>
		public void EnqueueSplit(int worldIdx) => RegionGraphMutator.EnqueueSplit(worldIdx);

		public JobHandle DoSyncAmbientToGrid(JobHandle dependency = default) => Maintenance.DoSyncAmbientToGrid(dependency);
		public JobHandle DoAggregateRoomData(JobHandle dependency = default) => Maintenance.DoAggregateRoomData(dependency);

		public void Dispose()
		{
			Logger.Dispose();
			Metabolism.Dispose();
			ExchangerBuffer.Dispose();
			State.Dispose();
			GasRegistry.Dispose();
			WindMap.Dispose();
			if (DirtyFaceIndices.IsCreated) DirtyFaceIndices.Dispose();
		}
	}
}
