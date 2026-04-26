using SolarWeb.Pneuma.Grid;
using Unity.Jobs;
using SolarWeb.Pneuma.Jobs.Diffusion;
using SolarWeb.Pneuma.Jobs.Thermal;
using SolarWeb.Pneuma.Logging;

namespace SolarWeb.Pneuma.Simulation
{
  /// <summary>
  /// Orchestrates the dynamic face diffusion pipeline each tick.
  /// Dynamic faces live in NativeLists on AtmosphereGrid; this class schedules
  /// and applies gas and thermal flux for all live dynamic faces.
  /// </summary>
  public class DynamicDiffusion
  {
    private const int HysteresisTicks = 10;

    private AtmosphereGrid State => manager.State;
    private readonly AtmosphereManager manager;

    public DynamicDiffusion(AtmosphereManager manager)
    {
      this.manager = manager;
    }

    public JobHandle DoGasDiffusion(float timeStep, JobHandle dependency = default)
    {
      int dynFaceCount = State.DynamicFaces.RegionA.Length;
      if (dynFaceCount == 0) return dependency;

      var job = new ComputeRegionGasDiffusion
      {
        MolarFractions = State.RegionPhysicsBuffer.MolarFractions,
        FaceGasFlux = State.DynamicFaces.GasFlux.AsArray(),
        FaceRegionA = State.DynamicFaces.RegionA.AsArray(),
        FaceRegionB = State.DynamicFaces.RegionB.AsArray(),
        FaceLimitedVel = State.DynamicFaces.LimitedVel.AsArray(),
        FaceMaxSafeAdvection = State.DynamicFaces.MaxSafeAdvection.AsArray(),
        FaceTFactor = State.DynamicFaces.TFactor.AsArray(),
        FaceGasPermeability = State.DynamicFaces.GasPermeability.AsArray(),
        FaceSurfaceArea = State.DynamicFaces.SurfaceArea.AsArray(),
        S_Constants = manager.GasRegistry.GasData.S_Constants,
        PressureKpa = State.RegionGasComposition.PressureKpa,
        GasCollisionDiameters = manager.GasRegistry.GasData.CollisionDiameter,
        MinCollisionDiameter = State.DynamicFaces.MinCollisionDiameter.AsArray(),
        MaxCollisionDiameter = State.DynamicFaces.MaxCollisionDiameter.AsArray(),
        FaceFlowDirection = State.DynamicFaces.FlowDirection.AsArray(),
        FaceActivePumpRate = State.DynamicFaces.ActivePumpRate.AsArray(),
        FaceMaxPumpPressureKpa = State.DynamicFaces.MaxPumpPressureKpa.AsArray(),
        FaceRegulatorKpa = State.DynamicFaces.RegulatorKpa.AsArray(),

        FaceWindExposureX = State.DynamicFaces.WindExposureX.AsArray(),
        FaceWindExposureZ = State.DynamicFaces.WindExposureZ.AsArray(),
        WindVelocity = manager.WindVelocity,

        TotalUMoles = State.RegionGasComposition.TotalUMoles,
        RegionFaceCounts = State.RegionFaceBuffer.RegionFaceCounts,
        DynRegionFaceCount = State.DynamicRegions.FaceCount,
        DynamicRegionPoolStart = State.DynamicRegionPoolStart,
        SentinelRegionIndex = State.SentinelRegionIndex,

        RegionStride = State.RegionStride,
        FaceStride = dynFaceCount,
        FaceCount = dynFaceCount,
        TimeStep = timeStep,
      };

      var handle = job.Schedule(State.GasCount, 1, dependency);
      return AtmosphereManager.CompleteIfNeeded(handle);
    }
    public JobHandle DoSumTotalFlux(JobHandle dependency = default)
    {
      int dynFaceCount = State.DynamicFaces.RegionA.Length;
      if (dynFaceCount == 0) return dependency;

      var job = new SumFaceTotalFlux
      {
        FaceGasFlux = State.DynamicFaces.GasFlux.AsArray(),
        FaceTotalGasFlux = State.DynamicFaces.TotalGasFlux.AsArray(),
        GasCount = State.GasCount,
        FaceStride = dynFaceCount
      };

      var handle = job.Schedule(dynFaceCount, 64, dependency);

      return AtmosphereManager.CompleteIfNeeded(handle);
    }

    public JobHandle DoAccumulateFlux(JobHandle dependency = default)
    {
      int dynFaceCount = State.DynamicFaces.RegionA.Length;
      if (dynFaceCount == 0) return dependency;

      var accumulateJob = new AccumulateRegionDiffusionFlux
      {
        FaceRegionA = State.DynamicFaces.RegionA.AsArray(),
        FaceRegionB = State.DynamicFaces.RegionB.AsArray(),
        FaceGasFlux = State.DynamicFaces.GasFlux.AsArray(),
        FaceTotalGasFlux = State.DynamicFaces.TotalGasFlux.AsArray(),
        TotalUMoles = State.RegionGasComposition.TotalUMoles,
        PreviousLiquidUMoles = State.LiquidComposition.PreviousuMoles,
        PendingGasDelta = State.RegionGasComposition.PendinguMolesDelta,
        PendingLiquidDelta = State.LiquidComposition.PendinguMolesDelta,
        GasCount = State.GasCount,
        RegionStride = State.RegionStride,
        FaceStride = dynFaceCount,
        FaceCount = dynFaceCount,
        SentinelIndex = State.SentinelRegionIndex
      };
      var handle = accumulateJob.Schedule(State.GasCount, 8, dependency);
      return AtmosphereManager.CompleteIfNeeded(handle);
    }

    public JobHandle DoThermalFlux(float timeStep, JobHandle dependency = default)
    {
      int dynFaceCount = State.DynamicFaces.RegionA.Length;
      if (dynFaceCount == 0) return dependency;

      var computeJob = new ComputeFaceThermalFlux
      {
        TemperatureK = State.RegionPhysicsBuffer.TemperatureK,
        RegionPressureKpa = State.RegionGasComposition.PressureKpa,
        FaceRegionA = State.DynamicFaces.RegionA.AsArray(),
        FaceRegionB = State.DynamicFaces.RegionB.AsArray(),
        FaceThermalConductivity = State.DynamicFaces.ThermalConductivity.AsArray(),
        TimeStep = timeStep,
        SentinelRegionIndex = State.SentinelRegionIndex,
        FaceThermalFlux = State.DynamicFaces.ThermalFlux.AsArray(),
      };

      var computeHandle = computeJob.Schedule(dynFaceCount, 32, dependency);

      var applyJob = new ApplyDynamicThermalFlux
      {
        FaceRegionA = State.DynamicFaces.RegionA.AsArray(),
        FaceRegionB = State.DynamicFaces.RegionB.AsArray(),
        FaceThermalFlux = State.DynamicFaces.ThermalFlux.AsArray(),
        MixtureMolarCp = State.RegionPhysicsBuffer.MixtureMolarCp,
        TotalUMoles = State.RegionGasComposition.TotalUMoles,
        RegionVolumes = State.RegionPhysicsBuffer.RegionVolumes,
        TemperatureK = State.RegionPhysicsBuffer.TemperatureK,
        FaceCount = dynFaceCount,
        SentinelRegionIndex = State.SentinelRegionIndex,
      };

      var applyHandle = applyJob.Schedule(computeHandle);
      return AtmosphereManager.CompleteIfNeeded(applyHandle);
    }

    public JobHandle DoAdvectiveThermal(float timeStep, JobHandle dependency = default)
    {
      int dynFaceCount = State.DynamicFaces.RegionA.Length;
      if (dynFaceCount == 0) return dependency;

      var job = new ApplyDynamicAdvectiveThermal
      {
        FaceRegionA = State.DynamicFaces.RegionA.AsArray(),
        FaceRegionB = State.DynamicFaces.RegionB.AsArray(),
        FaceGasFlux = State.DynamicFaces.GasFlux.AsArray(),
        GasMolarHeatCapacityCp = manager.GasRegistry.GasData.MolarHeatCapacityCp,
        TemperatureKSnapshot = State.RegionPhysicsBuffer.PreviousTemperatureK,
        MixtureMolarCp = State.RegionPhysicsBuffer.MixtureMolarCp,
        TotalUMoles = State.RegionGasComposition.TotalUMoles,
        PressureKpa = State.RegionGasComposition.PressureKpa,
        RegionVolumes = State.RegionPhysicsBuffer.RegionVolumes,
        TemperatureK = State.RegionPhysicsBuffer.TemperatureK,

        FaceActivePumpRate = State.DynamicFaces.ActivePumpRate.AsArray(),
        FaceFlowDirection = State.DynamicFaces.FlowDirection.AsArray(),
        FaceMaxPumpPressureKpa = State.DynamicFaces.MaxPumpPressureKpa.AsArray(),

        GasCount = State.GasCount,
        FaceCount = dynFaceCount,
        SentinelRegionIndex = State.SentinelRegionIndex,
        TimeStep = timeStep
      };

      var handle = job.Schedule(dependency);
      return AtmosphereManager.CompleteIfNeeded(handle);
    }

    public JobHandle DoDetectEquilibrium(JobHandle dependency = default)
    {
      var job = new DetectDynamicRegionEquilibrium
      {
        DynRegionParent = State.DynamicRegions.Parent,
        PressureKpa = State.RegionGasComposition.PressureKpa,
        TemperatureK = State.RegionPhysicsBuffer.TemperatureK,
        MolarFractions = State.RegionPhysicsBuffer.MolarFractions,
        EquilibriumTicks = State.RegionStates.EquilibriumTicks,
        DynamicRegionPoolStart = State.DynamicRegionPoolStart,
        MaxDynRegions = AtmosphereGrid.MaxDynRegions,
        GasCount = State.GasCount,
        RegionStride = State.RegionStride,
        SentinelRegionIndex = State.SentinelRegionIndex,
        RequiredEquilibriumTicks = 3,
        PressureTolerance = 0.01f,
        TemperatureTolerance = 0.5f,
        CompositionTolerance = 0.005f,
        MergeQueue = State.DynamicRegions.MergeQueue.AsParallelWriter(),
      };

      var handle = job.Schedule(dependency);
      return AtmosphereManager.CompleteIfNeeded(handle);
    }

    public JobHandle DoDetectBloom(JobHandle dependency = default)
    {
      int dynFaceCount = State.DynamicFaces.RegionA.Length;
      if (dynFaceCount == 0) return dependency;

      var job = new DetectDynamicRegionBloom
      {
        FaceRegionA = State.DynamicFaces.RegionA.AsArray(),
        FaceRegionB = State.DynamicFaces.RegionB.AsArray(),
        FaceSourceEdge = State.DynamicFaces.SourceEdge.AsArray(),
        PressureKpa = State.RegionGasComposition.PressureKpa,
        CellFaceCellA = State.GridLookups.CellFaceCellA,
        CellFaceCellB = State.GridLookups.CellFaceCellB,
        SimToWorldIndex = State.GridLookups.SimToWorldIndex,
        WorldToSimIndex = State.GridLookups.WorldToSimIndex,
        DynRegionWorldIdx = State.DynamicRegions.WorldIdx,
        DynamicRegionPoolStart = State.DynamicRegionPoolStart,
        SentinelRegionIndex = State.SentinelRegionIndex,
        SentinelCellIndex = State.SentinelCellIndex,
        BloomPressureRatio = 1.1f, // 10% pressure difference
        BloomAbsoluteKpa = 5.0f, // At least 5 kPa difference
        SplitQueue = State.DynamicRegions.PendingSplitWorldIndices.AsParallelWriter()
      };

      var handle = job.Schedule(dynFaceCount, 32, dependency);
      return AtmosphereManager.CompleteIfNeeded(handle);
    }
  }
}
