using SolarWeb.Pneuma.Grid;
using Unity.Jobs;
using SolarWeb.Pneuma.Jobs.Diffusion;
using SolarWeb.Pneuma.Jobs.Thermal;
using SolarWeb.Pneuma.Logging;

namespace SolarWeb.Pneuma.Simulation
{
  public class RegionDiffusion
  {
    private AtmosphereGrid State => manager.State;
    private readonly AtmosphereManager manager;

    public RegionDiffusion(AtmosphereManager manager)
    {
      this.manager = manager;
    }

    public JobHandle DoGasDiffusion(float timeStep, JobHandle dependency = default)
    {
      var job = new ComputeRegionGasDiffusion
      {
        MolarFractions = State.RegionPhysicsBuffer.MolarFractions,
        FaceGasFlux = State.RegionFaceBuffer.FaceGasFlux,

        FaceRegionA = State.RegionFaceBuffer.FaceRegionA,
        FaceRegionB = State.RegionFaceBuffer.FaceRegionB,
        FaceLimitedVel = State.RegionFaceBuffer.FaceLimitedVel,
        FaceMaxSafeAdvection = State.RegionFaceBuffer.FaceMaxSafeAdvection,
        FaceTFactor = State.RegionFaceBuffer.FaceTFactor,
        FaceGasPermeability = State.RegionFaceBuffer.FaceGasPermeability,
        FaceSurfaceArea = State.RegionFaceBuffer.FaceSurfaceArea,
        S_Constants = manager.GasRegistry.GasData.S_Constants,
        PressureKpa = State.RegionGasComposition.PressureKpa,
        TemperatureK = State.RegionPhysicsBuffer.TemperatureK,
        RegionInvVolConst = State.RegionPhysicsBuffer.InvVolConst,
        GasCollisionDiameters = manager.GasRegistry.GasData.CollisionDiameter,
        MinCollisionDiameter = State.RegionFaceBuffer.MinCollisionDiameter,
        MaxCollisionDiameter = State.RegionFaceBuffer.MaxCollisionDiameter,
        FaceFlowDirection = State.RegionFaceBuffer.FaceFlowDirection,
        FaceActivePumpRate = State.RegionFaceBuffer.FaceActivePumpRate,
        FaceMaxPumpPressureKpa = State.RegionFaceBuffer.FaceMaxPumpPressureKpa,
        FaceRegulatorKpa = State.RegionFaceBuffer.FaceRegulatorKpa,

        FaceWindExposureX = State.RegionFaceBuffer.FaceWindExposureX,
        FaceWindExposureZ = State.RegionFaceBuffer.FaceWindExposureZ,
        WindVelocity = manager.WindVelocity,

        TotalUMoles = State.RegionGasComposition.TotalUMoles,
        RegionFaceCounts = State.RegionFaceBuffer.RegionFaceCounts,
        DynRegionFaceCount = State.DynamicRegions.FaceCount,
        DynamicRegionPoolStart = State.DynamicRegionPoolStart,
        SentinelRegionIndex = State.SentinelRegionIndex,

        RegionStride = State.RegionStride,
        FaceStride = State.RegionFaceBuffer.FaceStride,
        FaceCount = State.RegionFaceBuffer.TotalFaceCount,
        TimeStep = timeStep,
      };

      var handle = job.Schedule(State.GasCount, 8, dependency);
      return AtmosphereManager.CompleteIfNeeded(handle);
    }

    public JobHandle DoSumTotalFlux(JobHandle dependency = default)
    {
      var job = new SumFaceTotalFlux
      {
        FaceGasFlux = State.RegionFaceBuffer.FaceGasFlux,
        FaceTotalGasFlux = State.RegionFaceBuffer.FaceTotalGasFlux,
        GasCount = State.GasCount,
        FaceStride = State.RegionFaceBuffer.FaceStride
      };

      var handle = job.Schedule(State.RegionFaceBuffer.TotalFaceCount, 64, dependency);
      return AtmosphereManager.CompleteIfNeeded(handle);
    }

    public JobHandle DoClearDeltas(JobHandle dependency = default)
    {
      var job = new ClearConservativeFluxDeltas
      {
        PendingGasDelta = State.RegionGasComposition.PendinguMolesDelta,
        PendingLiquidDelta = State.LiquidComposition.PendinguMolesDelta,
        PendingSolidDelta = State.SolidComposition.PendinguMolesDelta
      };
      var handle = job.Schedule(State.RegionGasComposition.PendinguMolesDelta.Length, 1024, dependency);
      return AtmosphereManager.CompleteIfNeeded(handle);
    }

    public JobHandle DoAccumulateFlux(JobHandle dependency = default)
    {
      var job = new AccumulateRegionDiffusionFlux
      {
        FaceRegionA = State.RegionFaceBuffer.FaceRegionA,
        FaceRegionB = State.RegionFaceBuffer.FaceRegionB,
        FaceGasFlux = State.RegionFaceBuffer.FaceGasFlux,
        FaceTotalGasFlux = State.RegionFaceBuffer.FaceTotalGasFlux,
        TotalUMoles = State.RegionGasComposition.TotalUMoles,
        PreviousLiquidUMoles = State.LiquidComposition.PreviousuMoles,
        PendingGasDelta = State.RegionGasComposition.PendinguMolesDelta,
        PendingLiquidDelta = State.LiquidComposition.PendinguMolesDelta,
        GasCount = State.GasCount,
        RegionStride = State.RegionStride,
        FaceStride = State.RegionFaceBuffer.FaceStride,
        FaceCount = State.RegionFaceBuffer.TotalFaceCount,
        SentinelIndex = State.SentinelRegionIndex
      };
      var handle = job.Schedule(State.GasCount, 8, dependency);
      return AtmosphereManager.CompleteIfNeeded(handle);
    }

    public JobHandle DoApplyConservativeFlux(JobHandle dependency = default)
    {
      var job = new ApplyConservativeRegionFlux
      {
        ActiveRegionIndices = State.RegionStates.ActiveIndices.AsDeferredJobArray(),
        PendingGasDelta = State.RegionGasComposition.PendinguMolesDelta,
        PendingLiquidDelta = State.LiquidComposition.PendinguMolesDelta,
        PendingSolidDelta = State.SolidComposition.PendinguMolesDelta,
        uMoles = State.RegionGasComposition.uMoles,
        LiquidUMoles = State.LiquidComposition.uMoles,
        SolidUMoles = State.SolidComposition.uMoles,
        RegionNetFlux = State.RegionGasComposition.RegionNetFlux,
        ActiveTicks = State.RegionStates.ActiveTicks,
        HysteresisTicks = 10,
        GasCount = State.GasCount,
        RegionStride = State.RegionStride,
        SentinelIndex = State.SentinelRegionIndex
      };

      var handle = job.Schedule(State.RegionStates.ActiveIndices, 32, dependency);
      return AtmosphereManager.CompleteIfNeeded(handle);
    }

    public JobHandle DoThermalFlux(float timeStep, JobHandle dependency = default)
    {
      var computeJob = new ComputeFaceThermalFlux
      {
        TemperatureK = State.RegionPhysicsBuffer.TemperatureK,
        FaceRegionA = State.RegionFaceBuffer.FaceRegionA,
        FaceRegionB = State.RegionFaceBuffer.FaceRegionB,
        FaceThermalConductivity = State.RegionFaceBuffer.FaceThermalConductivity,
        RegionPressureKpa = State.RegionGasComposition.PressureKpa,
        TimeStep = timeStep,
        SentinelRegionIndex = State.SentinelRegionIndex,
        FaceThermalFlux = State.RegionFaceBuffer.FaceThermalFlux,
      };

      var computeHandle = computeJob.Schedule(State.RegionFaceBuffer.TotalFaceCount, 64, dependency);

      var applyJob = new ApplyRegionThermalFlux
      {
        RegionFaceOffsets = State.RegionFaceBuffer.RegionFaceOffsets,
        RegionFaceCounts = State.RegionFaceBuffer.RegionFaceCounts,
        RegionFaceIndices = State.RegionFaceBuffer.RegionFaceIndices,
        FaceRegionA = State.RegionFaceBuffer.FaceRegionA,
        FaceThermalFlux = State.RegionFaceBuffer.FaceThermalFlux,
        MixtureMolarCp = State.RegionPhysicsBuffer.MixtureMolarCp,
        TotalUMoles = State.RegionGasComposition.TotalUMoles,
        RegionVolumes = State.RegionPhysicsBuffer.RegionVolumes,
        TemperatureK = State.RegionPhysicsBuffer.TemperatureK,
        ActiveRegionIndices = State.RegionStates.ActiveIndices.AsDeferredJobArray(),
        SentinelIndex = State.SentinelRegionIndex
      };

      var applyHandle = applyJob.Schedule(State.RegionStates.ActiveIndices, 32, computeHandle);
      return AtmosphereManager.CompleteIfNeeded(applyHandle);
    }

    public JobHandle DoAdvectiveThermal(float timeStep, JobHandle dependency = default)
    {
      var job = new ApplyRegionAdvectiveThermal
      {
        RegionFaceOffsets = State.RegionFaceBuffer.RegionFaceOffsets,
        RegionFaceCounts = State.RegionFaceBuffer.RegionFaceCounts,
        RegionFaceIndices = State.RegionFaceBuffer.RegionFaceIndices,
        FaceRegionA = State.RegionFaceBuffer.FaceRegionA,
        FaceRegionB = State.RegionFaceBuffer.FaceRegionB,
        FaceGasFlux = State.RegionFaceBuffer.FaceGasFlux,
        GasMolarHeatCapacityCp = manager.GasRegistry.GasData.MolarHeatCapacityCp,
        TemperatureKSnapshot = State.RegionPhysicsBuffer.PreviousTemperatureK,
        MixtureMolarCp = State.RegionPhysicsBuffer.MixtureMolarCp,
        TotalUMoles = State.RegionGasComposition.TotalUMoles,
        PressureKpa = State.RegionGasComposition.PressureKpa,
        RegionVolumes = State.RegionPhysicsBuffer.RegionVolumes,
        ActiveRegionIndices = State.RegionStates.ActiveIndices.AsDeferredJobArray(),
        TemperatureK = State.RegionPhysicsBuffer.TemperatureK,

        FaceActivePumpRate = State.RegionFaceBuffer.FaceActivePumpRate,
        FaceFlowDirection = State.RegionFaceBuffer.FaceFlowDirection,
        FaceMaxPumpPressureKpa = State.RegionFaceBuffer.FaceMaxPumpPressureKpa,

        GasCount = State.GasCount,
        FaceStride = State.RegionFaceBuffer.FaceStride,
        SentinelIndex = State.SentinelRegionIndex,
        TimeStep = timeStep
      };

      var handle = job.Schedule(State.RegionStates.ActiveIndices, 32, dependency);
      return AtmosphereManager.CompleteIfNeeded(handle);
    }

    public JobHandle DoStructuralThermalPasses(float timeStep, JobHandle dependency = default)
    {
      // 1. Compute face-to-face solid conduction (Enclosing structure only)
      var computeFaceJob = new ComputeEnclosingFaceThermalFlux
      {
        TemperatureK = State.RegionPhysicsBuffer.TemperatureK,
        EnclosingTemperatureK = State.RegionPhysicsBuffer.EnclosingTemperatureK,
        EnclosingThermalCapacity = State.RegionPhysicsBuffer.EnclosingThermalCapacity,
        FaceRegionA = State.RegionFaceBuffer.FaceRegionA,
        FaceRegionB = State.RegionFaceBuffer.FaceRegionB,
        FaceThermalConductivity = State.RegionFaceBuffer.FaceThermalConductivity,
        TimeStep = timeStep,
        SentinelRegionIndex = State.SentinelRegionIndex,
        EnclosingFaceThermalFlux = State.RegionFaceBuffer.EnclosingFaceThermalFlux
      };
      var faceHandle = computeFaceJob.Schedule(State.RegionFaceBuffer.TotalFaceCount, 64, dependency);

      // 2. Apply face-to-face solid conduction (Enclosing structure only)
      var applyFaceJob = new ApplyEnclosingFaceThermalFlux
      {
        RegionFaceOffsets = State.RegionFaceBuffer.RegionFaceOffsets,
        RegionFaceCounts = State.RegionFaceBuffer.RegionFaceCounts,
        RegionFaceIndices = State.RegionFaceBuffer.RegionFaceIndices,
        FaceRegionA = State.RegionFaceBuffer.FaceRegionA,
        EnclosingFaceThermalFlux = State.RegionFaceBuffer.EnclosingFaceThermalFlux,
        EnclosingThermalCapacity = State.RegionPhysicsBuffer.EnclosingThermalCapacity,
        EnclosingTemperatureK = State.RegionPhysicsBuffer.EnclosingTemperatureK,
        ActiveRegionIndices = State.RegionStates.ActiveIndices.AsDeferredJobArray(),
        SentinelIndex = State.SentinelRegionIndex
      };
      var applyFaceHandle = applyFaceJob.Schedule(State.RegionStates.ActiveIndices, 32, faceHandle);

      // 3. Compute gas-to-structure exchange (Both Enclosing and InternalMass)
      var computeExchJob = new ComputeStructureAndMassGasThermalExchange
      {
        EnclosingTemperatureK = State.RegionPhysicsBuffer.EnclosingTemperatureK,
        InternalMassTemperatureK = State.RegionPhysicsBuffer.InternalMassTemperatureK,
        TemperatureK = State.RegionPhysicsBuffer.TemperatureK,
        EnclosingThermalConductance = State.RegionPhysicsBuffer.EnclosingThermalConductance,
        InternalMassThermalConductance = State.RegionPhysicsBuffer.InternalMassThermalConductance,
        TimeStep = timeStep,
        ActiveRegionIndices = State.RegionStates.ActiveIndices.AsDeferredJobArray(),
        SentinelIndex = State.SentinelRegionIndex,
        EnclosingGasHeatFlux = State.RegionPhysicsBuffer.EnclosingGasHeatFlux,
        InternalMassGasHeatFlux = State.RegionPhysicsBuffer.InternalMassGasHeatFlux
      };
      var exchHandle = computeExchJob.Schedule(State.RegionStates.ActiveIndices, 32, applyFaceHandle);

      // 4. Apply gas-to-structure exchange (Both Enclosing and InternalMass)
      var applyExchJob = new ApplyStructureAndMassGasThermalExchange
      {
        EnclosingGasHeatFlux = State.RegionPhysicsBuffer.EnclosingGasHeatFlux,
        InternalMassGasHeatFlux = State.RegionPhysicsBuffer.InternalMassGasHeatFlux,
        MixtureMolarCp = State.RegionPhysicsBuffer.MixtureMolarCp,
        TotalUMoles = State.RegionGasComposition.TotalUMoles,
        EnclosingThermalCapacity = State.RegionPhysicsBuffer.EnclosingThermalCapacity,
        InternalMassThermalCapacity = State.RegionPhysicsBuffer.InternalMassThermalCapacity,
        RegionVolumes = State.RegionPhysicsBuffer.RegionVolumes,
        TemperatureK = State.RegionPhysicsBuffer.TemperatureK,
        EnclosingTemperatureK = State.RegionPhysicsBuffer.EnclosingTemperatureK,
        InternalMassTemperatureK = State.RegionPhysicsBuffer.InternalMassTemperatureK,
        ActiveRegionIndices = State.RegionStates.ActiveIndices.AsDeferredJobArray(),
        SentinelIndex = State.SentinelRegionIndex
      };
      var applyExchHandle = applyExchJob.Schedule(State.RegionStates.ActiveIndices, 32, exchHandle);

      return AtmosphereManager.CompleteIfNeeded(applyExchHandle);
    }
  }
}
