using SolarWeb.Pneuma.Grid;
using SolarWeb.Pneuma.Diffusion.Jobs;
using Unity.Jobs;
using SolarWeb.Pneuma.Jobs.Diffusion;
using SolarWeb.Pneuma.Jobs.Sync;
using SolarWeb.Pneuma.Logging;

namespace SolarWeb.Pneuma.Simulation
{
  public class RegionPhysics
  {
    private AtmosphereGrid State => manager.State;
    private readonly AtmosphereManager manager;

    public RegionPhysics(AtmosphereManager manager)
    {
      this.manager = manager;
    }

    public JobHandle DoSync(JobHandle dependency = default)
    {
      // Sync + precompute in one fused pass — totals/pressure/activity and all derived
      // physics (SpeedOfSound, TFactor, MolarFractions, etc.) streamed together.
      var syncHandle = new SyncAndPrecomputeRegions
      {
        uMoles = State.RegionGasComposition.uMoles,
        GasMolarMassesScaled = manager.GasRegistry.GasData.MolarMassScaled,
        RegionVolumes = State.RegionPhysicsBuffer.RegionVolumes,
        TemperatureK = State.RegionPhysicsBuffer.TemperatureK,
        EnclosingTemperatureK = State.RegionPhysicsBuffer.EnclosingTemperatureK,
        InternalMassTemperatureK = State.RegionPhysicsBuffer.InternalMassTemperatureK,
        MolarHeatCapacityAtConstantPressure = manager.GasRegistry.GasData.MolarHeatCapacityCp,
        MolarHeatCapacityAtConstantVolume = manager.GasRegistry.GasData.MolarHeatCapacityCv,
        AmbientTemp = State.AmbientEnvironment.TemperatureK,
        IsBurning = State.RegionStates.IsBurning,
        SentinelIndex = State.SentinelRegionIndex,

        TotalUMoles = State.RegionGasComposition.TotalUMoles,
        AverageMolarMass = State.RegionPhysicsBuffer.AverageMolarMass,
        PressureKpa = State.RegionGasComposition.PressureKpa,
        PreviousPressureKpa = State.RegionGasComposition.PreviousPressureKpa,
        PreviousTemperatureK = State.RegionPhysicsBuffer.PreviousTemperatureK,
        PreviousEnclosingTemperatureK = State.RegionPhysicsBuffer.PreviousEnclosingTemperatureK,
        PreviousInternalMassTemperatureK = State.RegionPhysicsBuffer.PreviousInternalMassTemperatureK,
        ActiveTicks = State.RegionStates.ActiveTicks,
        RegionIsActive = State.RegionStates.IsActive,

        InvTotalUMoles = State.RegionGasComposition.InvTotalUMoles,
        RegionSpeedOfSound = State.RegionPhysicsBuffer.SpeedOfSound,
        RegionInvVolConst = State.RegionPhysicsBuffer.InvVolConst,
        RegionTFactor = State.RegionPhysicsBuffer.TFactor,
        MixtureMolarCp = State.RegionPhysicsBuffer.MixtureMolarCp,
        MolarFractions = State.RegionPhysicsBuffer.MolarFractions,

        GasCount = State.GasCount,
        RegionStride = State.RegionStride,
        PressureDirtyThreshold = 0.001f,
        TemperatureDirtyThreshold = 0.5f,
        HysteresisTicks = 5,
      }.ScheduleBatch(State.SentinelRegionIndex + 1, SyncAndPrecomputeRegions.MaxBatch, dependency);

      // Snapshot liquid + solid condensable rows only (non-condensable rows are always zero).
      var snapshotHandle = new CopyPhaseSnapshots
      {
        LiquidSrc = State.LiquidComposition.uMoles,
        LiquidDst = State.LiquidComposition.PreviousuMoles,
        SolidSrc = State.SolidComposition.uMoles,
        SolidDst = State.SolidComposition.PreviousuMoles,
        CondensableGasIndices = manager.GasRegistry.GasData.CondensableGasIndices,
        CondensableGasCount = manager.GasRegistry.GasData.CondensableGasCount,
        RegionStride = State.RegionStride,
      }.Schedule(dependency);

      var combined = JobHandle.CombineDependencies(syncHandle, snapshotHandle);
      return AtmosphereManager.CompleteIfNeeded(combined);
    }

    public JobHandle DoGatherActiveRegions(JobHandle dependency = default)
    {
      State.RegionStates.ActiveIndices.Clear();
      State.RegionStates.ActiveCellIndices.Clear();

      var activeListParallel = State.RegionStates.ActiveIndices.AsParallelWriter();
      var activeCellListParallel = State.RegionStates.ActiveCellIndices.AsParallelWriter();

      var handle = new GatherActiveIndices
      {
        IsActive = State.RegionStates.IsActive,
        ActiveTicks = State.RegionStates.ActiveTicks,
        DynamicRegionPoolStart = State.DynamicRegionPoolStart,
        SentinelIndex = State.SentinelRegionIndex,
        ActiveList = activeListParallel,
      }.Schedule(State.SentinelRegionIndex + 1, 64, dependency);

      var cellHandle = new GatherActiveCellIndices
      {
        IsActive = State.RegionStates.IsActive,
        SimToWorldIndex = State.GridLookups.SimToWorldIndex,
        WorldToRegionIndex = State.GridLookups.WorldToRegionIndex,
        SentinelRegionIndex = State.SentinelRegionIndex,
        ActiveCellList = activeCellListParallel
      }.Schedule(State.Stride, 64, dependency);

      return JobHandle.CombineDependencies(handle, cellHandle);
    }

    public JobHandle DoPrecomputeDynamicFacePhysics(float timeStep, JobHandle dependency = default)
    {
      int dynFaceCount = State.DynamicFaces.RegionA.Length;
      if (dynFaceCount == 0) return dependency;

      var handle = new PrecomputeFacePhysics
      {
        PressureKpa = State.RegionGasComposition.PressureKpa,
        TemperatureK = State.RegionPhysicsBuffer.TemperatureK,
        RegionSpeedOfSound = State.RegionPhysicsBuffer.SpeedOfSound,
        RegionInvVolConst = State.RegionPhysicsBuffer.InvVolConst,
        RegionTFactor = State.RegionPhysicsBuffer.TFactor,
        TotalUMoles = State.RegionGasComposition.TotalUMoles,
        FaceRegionA = State.DynamicFaces.RegionA.AsArray(),
        FaceRegionB = State.DynamicFaces.RegionB.AsArray(),
        FaceGasPermeability = State.DynamicFaces.GasPermeability.AsArray(),
        FaceRegulatorKpa = State.DynamicFaces.RegulatorKpa.AsArray(),
        FaceSurfaceArea = State.DynamicFaces.SurfaceArea.AsArray(),
        RegionFaceCounts = State.RegionFaceBuffer.RegionFaceCounts,
        DynRegionFaceCount = State.DynamicRegions.FaceCount,
        DynamicRegionPoolStart = State.DynamicRegionPoolStart,
        FacePressureOffset = State.DynamicFaces.FacePressureOffsetKpa.AsArray(),
        SentinelRegionIndex = State.SentinelRegionIndex,
        FaceLimitedVel = State.DynamicFaces.LimitedVel.AsArray(),
        FaceMaxSafeAdvection = State.DynamicFaces.MaxSafeAdvection.AsArray(),
        FaceCombinedTFactor = State.DynamicFaces.TFactor.AsArray(),
        TimeStep = timeStep,
        FaceActivePumpRate = State.DynamicFaces.ActivePumpRate.AsArray(),
      }.Schedule(dynFaceCount, 32, dependency);

      return AtmosphereManager.CompleteIfNeeded(handle);
    }

    public JobHandle DoPrecomputeFacePhysics(float timeStep, JobHandle dependency = default)
    {
      var handle = new PrecomputeFacePhysics
      {
        PressureKpa = State.RegionGasComposition.PressureKpa,
        TemperatureK = State.RegionPhysicsBuffer.TemperatureK,
        RegionSpeedOfSound = State.RegionPhysicsBuffer.SpeedOfSound,
        RegionInvVolConst = State.RegionPhysicsBuffer.InvVolConst,
        RegionTFactor = State.RegionPhysicsBuffer.TFactor,
        TotalUMoles = State.RegionGasComposition.TotalUMoles,

        FaceRegionA = State.RegionFaceBuffer.FaceRegionA,
        FaceRegionB = State.RegionFaceBuffer.FaceRegionB,

        FaceGasPermeability = State.RegionFaceBuffer.FaceGasPermeability,
        FaceRegulatorKpa = State.RegionFaceBuffer.FaceRegulatorKpa,
        FaceSurfaceArea = State.RegionFaceBuffer.FaceSurfaceArea,
        RegionFaceCounts = State.RegionFaceBuffer.RegionFaceCounts,
        DynRegionFaceCount = State.DynamicRegions.FaceCount,
        DynamicRegionPoolStart = State.DynamicRegionPoolStart,
        FacePressureOffset = State.RegionFaceBuffer.FacePressureOffsetKpa,
        FaceCombinedTFactor = State.RegionFaceBuffer.FaceTFactor,
        FaceLimitedVel = State.RegionFaceBuffer.FaceLimitedVel,
        FaceMaxSafeAdvection = State.RegionFaceBuffer.FaceMaxSafeAdvection,
        SentinelRegionIndex = State.SentinelRegionIndex,
        TimeStep = timeStep,
        FaceActivePumpRate = State.RegionFaceBuffer.FaceActivePumpRate,
      }.Schedule(State.RegionFaceBuffer.TotalFaceCount, 64, dependency);

      return AtmosphereManager.CompleteIfNeeded(handle);
    }
  }
}
