using System;
using SolarWeb.Pneuma.Grid;
using SolarWeb.Pneuma.Data;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using SolarWeb.Pneuma.Jobs.UI;

namespace SolarWeb.Pneuma.Simulation.UI
{
	public class AtmosphereVisualsHandler : IDisposable
	{
		private readonly AtmosphereManager manager;
		private AtmosphereGrid State => manager.State;

		public NativeArray<Color32> PressurePixelBuffer;
		public NativeArray<Color32> DangerPixelBuffer;
		public NativeArray<Color32> SpectrographPixelBuffer;

		public NativeArray<Color32> PressureLUT;
		public NativeArray<Color32> DangerLUT;
		public NativeArray<Color32> SpectrographLUT;

		public NativeList<FlowArrowData> FlowArrows;
		public NativeList<StressPointData> StressPoints;
		public NativeList<ThermalPointData> ThermalPoints;
		public NativeList<AmbientGasPointData> AmbientGasPoints;
		public NativeList<PhaseChangePointData> PhaseChangePoints;

		public bool PressureVisualsRequested { get; set; }
		public bool DangerVisualsRequested { get; set; }
		public bool FlowVisualsRequested { get; set; }
		public bool SpectrographVisualsRequested { get; set; }
		public bool VFXVisualsRequested { get; set; }

		public int SelectedMetabolismBatchIdx { get; set; } = -1;
		public int SelectedGasId { get; set; } = -1;

		public bool IsPressureDataReady { get; private set; }
		public bool IsDangerDataReady { get; private set; }
		public bool IsFlowDataReady { get; private set; }
		public bool IsSpectrographDataReady { get; private set; }
		public bool IsVFXDataReady { get; private set; }

		public bool ConsumePressureDataReady() { return IsPressureDataReady; }
		public bool ConsumeDangerDataReady() { return IsDangerDataReady; }
		public bool ConsumeFlowDataReady() { return IsFlowDataReady; }
		public bool ConsumeSpectrographDataReady() { return IsSpectrographDataReady; }
		public bool ConsumeVFXDataReady() { return IsVFXDataReady; }

		private JobHandle lastPressureJob;
		private JobHandle lastDangerJob;
		private JobHandle lastFlowJob;
		private JobHandle lastSpectrographJob;
		private JobHandle lastVFXJob;

		public AtmosphereVisualsHandler(AtmosphereManager manager)
		{
			this.manager = manager;

			int cellCount = State.MapWidth * State.MapHeight;
			PressurePixelBuffer = new NativeArray<Color32>(cellCount, Allocator.Persistent);
			DangerPixelBuffer = new NativeArray<Color32>(cellCount, Allocator.Persistent);
			SpectrographPixelBuffer = new NativeArray<Color32>(cellCount, Allocator.Persistent);

			FlowArrows = new NativeList<FlowArrowData>(1024, Allocator.Persistent);
			StressPoints = new NativeList<StressPointData>(512, Allocator.Persistent);
			ThermalPoints = new NativeList<ThermalPointData>(512, Allocator.Persistent);
			AmbientGasPoints = new NativeList<AmbientGasPointData>(1024, Allocator.Persistent);
			PhaseChangePoints = new NativeList<PhaseChangePointData>(512, Allocator.Persistent);

			PressureLUT = new NativeArray<Color32>(256, Allocator.Persistent);
			DangerLUT = new NativeArray<Color32>(256, Allocator.Persistent);
			SpectrographLUT = new NativeArray<Color32>(256, Allocator.Persistent);
			InitializeLUTs();
		}

		private void InitializeLUTs()
		{
			for (int i = 0; i < 256; i++)
			{
				float normalized = i / 255f;
				float t = normalized * 4.0f;
				Color c;
				if (t <= 1.0f) c = Color.Lerp(Color.blue, Color.cyan, t);
				else if (t <= 2.0f) c = Color.Lerp(Color.cyan, Color.green, t - 1.0f);
				else if (t <= 3.0f) c = Color.Lerp(Color.green, Color.yellow, t - 2.0f);
				else c = Color.Lerp(Color.yellow, Color.red, t - 3.0f);
				PressureLUT[i] = c;
			}

			for (int i = 0; i < 256; i++)
			{
				float normalized = i / 255f;
				Color c;
				if (normalized <= 0.5f) c = Color.Lerp(new Color(1, 1, 0, 0), Color.yellow, normalized * 2f);
				else c = Color.Lerp(Color.yellow, Color.red, (normalized - 0.5f) * 2f);
				DangerLUT[i] = c;
			}

			for (int i = 0; i < 256; i++)
			{
				float normalized = i / 255f;
				SpectrographLUT[i] = Color.Lerp(new Color(1, 1, 1, 0), Color.white, normalized);
			}
		}

		public JobHandle ScheduleVisualsJobs(JobHandle dependency)
		{
			IsPressureDataReady = false;
			IsDangerDataReady = false;
			IsFlowDataReady = false;
			IsSpectrographDataReady = false;
			IsVFXDataReady = false;

			JobHandle combined = dependency;

			if (PressureVisualsRequested)
			{
				int requiredSize = State.MapWidth * State.MapHeight;
				if (!PressurePixelBuffer.IsCreated || PressurePixelBuffer.Length != requiredSize)
				{
					if (PressurePixelBuffer.IsCreated) PressurePixelBuffer.Dispose();
					PressurePixelBuffer = new NativeArray<Color32>(requiredSize, Allocator.Persistent);
				}

				const float AtmosphereLow = 57f;
				const float StandardAtmosphereKpa = 101.325f;
				const float MaxPressureRange = (StandardAtmosphereKpa * 1.5f) - AtmosphereLow;

				var job = new UpdatePressureTexture
				{
					WorldToRegionIndex = State.GridLookups.WorldToRegionIndex,
					RegionPressureKpa = State.RegionGasComposition.PressureKpa,
					ColorLookupTable = PressureLUT,
					OutPixels = PressurePixelBuffer,
					DynamicRegionPoolStart = State.DynamicRegionPoolStart,
					SentinelRegionIndex = State.SentinelRegionIndex,
					AtmosphereLow = AtmosphereLow,
					MaxPressureRange = MaxPressureRange
				};

				lastPressureJob = job.Schedule(PressurePixelBuffer.Length, 128, dependency);
				combined = JobHandle.CombineDependencies(combined, lastPressureJob);
			}

			if (DangerVisualsRequested && manager.Metabolism?.CellDangerLevels.IsCreated == true)
			{
				int requiredSize = State.MapWidth * State.MapHeight;
				if (!DangerPixelBuffer.IsCreated || DangerPixelBuffer.Length != requiredSize)
				{
					if (DangerPixelBuffer.IsCreated) DangerPixelBuffer.Dispose();
					DangerPixelBuffer = new NativeArray<Color32>(requiredSize, Allocator.Persistent);
				}

				var job = new UpdateDangerTexture
				{
					WorldToSimIndex = State.GridLookups.WorldToSimIndex,
					CellDangerLevels = manager.Metabolism.CellDangerLevels,
					ColorLookupTable = DangerLUT,
					OutPixels = DangerPixelBuffer,
					SelectedBatchIdx = SelectedMetabolismBatchIdx,
					Stride = State.Stride,
					SentinelCellIndex = State.SentinelCellIndex
				};

				lastDangerJob = job.Schedule(DangerPixelBuffer.Length, 128, dependency);
				combined = JobHandle.CombineDependencies(combined, lastDangerJob);
			}

			if (SpectrographVisualsRequested && SelectedGasId >= 0)
			{
				int requiredSize = State.MapWidth * State.MapHeight;
				if (!SpectrographPixelBuffer.IsCreated || SpectrographPixelBuffer.Length != requiredSize)
				{
					if (SpectrographPixelBuffer.IsCreated) SpectrographPixelBuffer.Dispose();
					SpectrographPixelBuffer = new NativeArray<Color32>(requiredSize, Allocator.Persistent);
				}

				var job = new UpdateSpectrographTexture
				{
					WorldToRegionIndex = State.GridLookups.WorldToRegionIndex,
					MolarFractions = State.RegionPhysicsBuffer.MolarFractions,
					ColorLookupTable = SpectrographLUT,
					OutPixels = SpectrographPixelBuffer,
					SelectedGasId = SelectedGasId,
					Stride = State.RegionStride,
					SentinelRegionIndex = State.SentinelRegionIndex
				};

				lastSpectrographJob = job.Schedule(SpectrographPixelBuffer.Length, 128, dependency);
				combined = JobHandle.CombineDependencies(combined, lastSpectrographJob);
			}

			if (FlowVisualsRequested)
			{
				FlowArrows.Clear();
				var job = new UpdateFlowArrows
				{
					FaceRegionA = State.RegionFaceBuffer.FaceRegionA,
					FaceRegionB = State.RegionFaceBuffer.FaceRegionB,
					FaceType = State.RegionFaceBuffer.FaceType,
					FaceGasPermeability = State.RegionFaceBuffer.FaceGasPermeability,
					FaceGasFlux = State.RegionFaceBuffer.FaceGasFlux,
					RegionFaceToCellOffsets = State.RegionFaceBuffer.RegionFaceToCellOffsets,
					RegionFaceToCellCounts = State.RegionFaceBuffer.RegionFaceToCellCounts,
					RegionFaceToCellSimA = State.RegionFaceBuffer.RegionFaceToCellSimA,
					RegionFaceToCellSimB = State.RegionFaceBuffer.RegionFaceToCellSimB,
					FaceDirection = State.RegionFaceBuffer.FaceDirection,
					SimToWorldIndex = State.GridLookups.SimToWorldIndex,
					WorldToSimIndex = State.GridLookups.WorldToSimIndex,
					GasOverlayColor = manager.GasRegistry.GasData.OverlayColor,
					GasMolarMass = manager.GasRegistry.GasData.MolarMass,

					DynFaceRegionA = State.DynamicFaces.RegionA.AsArray(),
					DynFaceRegionB = State.DynamicFaces.RegionB.AsArray(),
					DynFaceType = State.DynamicFaces.Type.AsArray(),
					DynFaceGasPermeability = State.DynamicFaces.GasPermeability.AsArray(),
					DynFaceGasFlux = State.DynamicFaces.GasFlux.AsArray(),
					DynFaceSourceEdge = State.DynamicFaces.SourceEdge.AsArray(),
					CellFaceDirection = State.GridLookups.CellFaceDirection,
					CellFaceCellA = State.GridLookups.CellFaceCellA,
					CellFaceCellB = State.GridLookups.CellFaceCellB,

					RegionVolumes = State.RegionPhysicsBuffer.RegionVolumes,

					OutArrows = FlowArrows,
					FaceStride = State.RegionFaceBuffer.FaceStride,
					GasCount = State.GasCount,
					MapWidth = State.MapWidth,
					MapHeight = State.MapHeight,
					SentinelRegionIndex = State.SentinelRegionIndex,
					SentinelCellIndex = State.SentinelCellIndex,
					VisibilityThresholdUMol = 5000f,
					MinPermeabilityThreshold = 0.0001f
				};

				lastFlowJob = job.Schedule(dependency);
				combined = JobHandle.CombineDependencies(combined, lastFlowJob);
			}

			if (VFXVisualsRequested)
			{
				StressPoints.Clear();
				ThermalPoints.Clear();
				AmbientGasPoints.Clear();
				PhaseChangePoints.Clear();

				var stressJob = new PrepareStressVFX
				{
					FaceRegionA = State.RegionFaceBuffer.FaceRegionA,
					FaceRegionB = State.RegionFaceBuffer.FaceRegionB,
					FaceMaxPressureDeltaKpa = State.RegionFaceBuffer.FaceMaxPressureDeltaKpa,
					RegionPressureKpa = State.RegionGasComposition.PressureKpa,
					RegionFaceToCellOffsets = State.RegionFaceBuffer.RegionFaceToCellOffsets,
					RegionFaceToCellCounts = State.RegionFaceBuffer.RegionFaceToCellCounts,
					RegionFaceToCellSimA = State.RegionFaceBuffer.RegionFaceToCellSimA,
					RegionFaceToCellSimB = State.RegionFaceBuffer.RegionFaceToCellSimB,
					SimToWorldIndex = State.GridLookups.SimToWorldIndex,
					OutStressPoints = StressPoints,
					FaceCount = State.RegionFaceBuffer.TotalFaceCount,
					MapWidth = State.MapWidth,
					SentinelRegionIndex = State.SentinelRegionIndex,
					SentinelCellIndex = State.SentinelCellIndex
				};

				var thermalJob = new PrepareThermalVFXJob
				{
					TemperatureK = State.RegionPhysicsBuffer.TemperatureK,
					ActiveRegionIndices = State.RegionStates.ActiveIndices.AsArray(),
					RegionMinX = State.RegionStates.MinX,
					RegionMaxX = State.RegionStates.MaxX,
					RegionMinZ = State.RegionStates.MinZ,
					RegionMaxZ = State.RegionStates.MaxZ,
					OutThermalPoints = ThermalPoints,
					ActiveRegionCount = State.RegionStates.ActiveIndices.Length
				};

				var ambientJob = new PrepareAmbientGasVFXJob
				{
					ActiveRegionIndices = State.RegionStates.ActiveIndices.AsArray(),
					MolarFractions = State.RegionPhysicsBuffer.MolarFractions,
					TotalUMoles = State.RegionGasComposition.TotalUMoles,
					GasOverlayColor = manager.GasRegistry.GasData.OverlayColor,
					GasMolarMass = manager.GasRegistry.GasData.MolarMass,
					RegionMinX = State.RegionStates.MinX,
					RegionMaxX = State.RegionStates.MaxX,
					RegionMinZ = State.RegionStates.MinZ,
					RegionMaxZ = State.RegionStates.MaxZ,
					OutAmbientPoints = AmbientGasPoints,
					ActiveRegionCount = State.RegionStates.ActiveIndices.Length,
					GasCount = State.GasCount,
					RegionStride = State.RegionStride,
					VisibilityThresholdMoles = 1.0f
				};

				var stressHandle = stressJob.Schedule(dependency);
				var thermalHandle = thermalJob.Schedule(dependency);
				var ambientHandle = ambientJob.Schedule(dependency);

				var phaseChangeJob = new PreparePhaseChangeVFXJob
				{
					ActiveRegionIndices = State.RegionStates.ActiveIndices.AsArray(),
					SolidUMoles = State.SolidComposition.uMoles,
					PreviousSolidUMoles = State.SolidComposition.PreviousuMoles,
					LiquidUMoles = State.LiquidComposition.uMoles,
					PreviousLiquidUMoles = State.LiquidComposition.PreviousuMoles,
					GasOverlayColor = manager.GasRegistry.GasData.OverlayColor,
					RegionMinX = State.RegionStates.MinX,
					RegionMaxX = State.RegionStates.MaxX,
					RegionMinZ = State.RegionStates.MinZ,
					RegionMaxZ = State.RegionStates.MaxZ,
					OutPhaseChangePoints = PhaseChangePoints,
					ActiveRegionCount = State.RegionStates.ActiveIndices.Length,
					GasCount = State.GasCount,
					RegionStride = State.RegionStride,
					VisibilityThresholdMoles = 0.1f
				};

				var phaseChangeHandle = phaseChangeJob.Schedule(dependency);

				lastVFXJob = JobHandle.CombineDependencies(stressHandle, thermalHandle, ambientHandle);
				lastVFXJob = JobHandle.CombineDependencies(lastVFXJob, phaseChangeHandle);

				combined = JobHandle.CombineDependencies(combined, lastVFXJob);
			}

			return combined;
		}

		public void FinalizeVisuals()
		{
			if (PressureVisualsRequested) { lastPressureJob.Complete(); IsPressureDataReady = true; }
			if (DangerVisualsRequested) { lastDangerJob.Complete(); IsDangerDataReady = true; }
			if (FlowVisualsRequested) { lastFlowJob.Complete(); IsFlowDataReady = true; }
			if (SpectrographVisualsRequested) { lastSpectrographJob.Complete(); IsSpectrographDataReady = true; }
			if (VFXVisualsRequested) { lastVFXJob.Complete(); IsVFXDataReady = true; }
		}

		public void Dispose()
		{
			if (PressurePixelBuffer.IsCreated) PressurePixelBuffer.Dispose();
			if (DangerPixelBuffer.IsCreated) DangerPixelBuffer.Dispose();
			if (SpectrographPixelBuffer.IsCreated) SpectrographPixelBuffer.Dispose();
			if (PressureLUT.IsCreated) PressureLUT.Dispose();
			if (DangerLUT.IsCreated) DangerLUT.Dispose();
			if (SpectrographLUT.IsCreated) SpectrographLUT.Dispose();
			if (FlowArrows.IsCreated) FlowArrows.Dispose();
			if (StressPoints.IsCreated) StressPoints.Dispose();
			if (ThermalPoints.IsCreated) ThermalPoints.Dispose();
			if (AmbientGasPoints.IsCreated) AmbientGasPoints.Dispose();
			if (PhaseChangePoints.IsCreated) PhaseChangePoints.Dispose();
		}
	}
}
