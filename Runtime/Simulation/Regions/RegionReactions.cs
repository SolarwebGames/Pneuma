using Unity.Jobs;

using SolarWeb.Pneuma.Grid;
using SolarWeb.Pneuma.Jobs.Combustion;
using SolarWeb.Pneuma.Jobs.PhaseTransition;
using SolarWeb.Pneuma.Logging;

namespace SolarWeb.Pneuma.Simulation
{
  public class RegionReactions
  {
    private AtmosphereGrid State => manager.State;
    private readonly AtmosphereManager manager;

    public RegionReactions(AtmosphereManager manager)
    {
      this.manager = manager;
    }

    public JobHandle DoProcessCombustion(float timeStep, JobHandle dependency = default)
    {
      var job = new ProcessRegionCombustion
      {
        ActiveRegionIndices = State.RegionStates.ActiveIndices.AsDeferredJobArray(),
        uMoles = State.RegionGasComposition.uMoles,
        TotalUMoles = State.RegionGasComposition.TotalUMoles,
        EffectiveTotalUMoles = State.RegionGasComposition.TotalUMoles,
        TemperatureK = State.RegionPhysicsBuffer.TemperatureK,
        IsBurning = State.RegionStates.IsBurning,
        BurnIntensity = State.RegionStates.BurnIntensity,

        AutoIgnitionTemperature = manager.GasRegistry.GasData.AutoIgnitionTemperature,
        OxidizingPotency = manager.GasRegistry.GasData.OxidizingPotency,
        LowerExplosiveLimit = manager.GasRegistry.GasData.LowerExplosiveLimit,
        UpperExplosiveLimit = manager.GasRegistry.GasData.UpperExplosiveLimit,
        EnthalpyOfCombustion_Jmol = manager.GasRegistry.GasData.EnthalpyOfCombustion_Jmol,

        AtomicCompositionIndices = manager.GasRegistry.GasData.AtomicCompositionIndices,
        CompositionOffsets = manager.GasRegistry.GasData.CompositionOffsets,
        ElementOxideGasId = manager.GasRegistry.GasData.ElementOxideGasId,
        ElementOxideStoichiometry = manager.GasRegistry.GasData.ElementOxideStoichiometry,

        MixtureMolarCp = State.RegionPhysicsBuffer.MixtureMolarCp,
        PressureKpa = State.RegionGasComposition.PressureKpa,

        GasCount = State.GasCount,
        RegionStride = State.RegionStride,
        TimeStep = timeStep,
        SentinelIndex = State.SentinelRegionIndex,
        MinCombustionPressureKpa = 5.0f,
      };

      var handle = job.Schedule(State.RegionStates.ActiveIndices, 1, dependency);
      return AtmosphereManager.CompleteIfNeeded(handle);
    }

    public JobHandle DoPhaseTransitions(float timeStep, JobHandle dependency = default)
    {
      var job = new ComputePhaseTransitions
      {
        ActiveRegionIndices = State.RegionStates.ActiveIndices.AsDeferredJobArray(),
        TotalUMoles = State.RegionGasComposition.TotalUMoles,
        PressureKpa = State.RegionGasComposition.PressureKpa,
        TemperatureK = State.RegionPhysicsBuffer.TemperatureK,
        AntoineA = manager.GasRegistry.GasData.AntoineA,
        AntoineB = manager.GasRegistry.GasData.AntoineB,
        AntoineC = manager.GasRegistry.GasData.AntoineC,
        MixtureMolarCp = State.RegionPhysicsBuffer.MixtureMolarCp,
        RegionVolumes = State.RegionPhysicsBuffer.RegionVolumes,
        GasUMoles = State.RegionGasComposition.uMoles,
        LiquidUMoles = State.LiquidComposition.uMoles,
        GasCount = State.GasCount,
        RegionStride = State.RegionStride,
        SentinelIndex = State.SentinelRegionIndex,
        TimeStep = timeStep,
        EvaporationRate = 2.0f,
        CondensationRate = 2.0f,
      };

      var handle = job.Schedule(State.RegionStates.ActiveIndices, 8, dependency);
      return AtmosphereManager.CompleteIfNeeded(handle);
    }

    public JobHandle DoSolidPhaseTransitions(float timeStep, JobHandle dependency = default)
    {
      var job = new ComputeSolidPhaseTransitions
      {
        ActiveRegionIndices = State.RegionStates.ActiveIndices.AsDeferredJobArray(),
        TemperatureK = State.RegionPhysicsBuffer.TemperatureK,
        MeltingPointK = manager.GasRegistry.GasData.MeltingPoint_K,
        LiquidUMoles = State.LiquidComposition.uMoles,
        SolidUMoles = State.SolidComposition.uMoles,
        GasCount = State.GasCount,
        RegionStride = State.RegionStride,
        SentinelIndex = State.SentinelRegionIndex,
        TimeStep = timeStep,
        FreezingRate = 1.0f,
        MeltingRate = 1.0f,
      };

      var handle = job.Schedule(State.RegionStates.ActiveIndices, 8, dependency);
      return AtmosphereManager.CompleteIfNeeded(handle);
    }

    public JobHandle DoDetectIgnition(JobHandle dependency = default)
    {
      var handle = new DetectIgnition
      {
        ActiveRegionIndices = State.RegionStates.ActiveIndices.AsDeferredJobArray(),
        uMoles = State.RegionGasComposition.uMoles,
        TotalUMoles = State.RegionGasComposition.TotalUMoles,
        TemperatureK = State.RegionPhysicsBuffer.TemperatureK,
        PressureKpa = State.RegionGasComposition.PressureKpa,

        LowerExplosiveLimit = manager.GasRegistry.GasData.LowerExplosiveLimit,
        UpperExplosiveLimit = manager.GasRegistry.GasData.UpperExplosiveLimit,
        AutoIgnitionTemperature = manager.GasRegistry.GasData.AutoIgnitionTemperature,
        OxidizingPotency = manager.GasRegistry.GasData.OxidizingPotency,
        IsBurning = State.RegionStates.IsBurning,

        GasCount = manager.GasRegistry.GasCount,
        RegionStride = State.RegionStride,
        MinOxidizingPotency = 0.1f, // Equivalent to 10% O2
        MinCombustionPressureKpa = 5.0f,
        IgnitionEvents = State.Events.IgnitionEvents.AsParallelWriter()
      }.Schedule(State.RegionStates.ActiveIndices, 32, dependency);

      return AtmosphereManager.CompleteIfNeeded(handle);
    }
  }
}
