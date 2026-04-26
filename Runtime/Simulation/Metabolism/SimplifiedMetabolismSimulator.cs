using SolarWeb.Pneuma.Grid;
using SolarWeb.Pneuma.Jobs.Metabolism;
using SolarWeb.Pneuma.Logging;
using SolarWeb.Pneuma.Metabolism;
using Unity.Jobs;

namespace SolarWeb.Pneuma.Simulation
{
  public class SimplifiedMetabolismSimulator
  {
    private AtmosphereGrid State => manager.State;
    private readonly AtmosphereManager manager;
    private int ticksSinceLastUpdate = 0;

    public SimplifiedMetabolismSimulator(AtmosphereManager manager)
    {
      this.manager = manager;
    }

    public JobHandle DoUpdate(float timeStep, JobHandle dependency = default)
    {
      ticksSinceLastUpdate++;

      JobHandle combined = dependency;
      bool anyRan = false;

      foreach (var batchInterface in manager.Metabolism.Batches.Values)
      {
        if (!batchInterface.IsSimplified) continue;

        var batch = (SimplifiedMetabolismBatch)batchInterface;
        if (batch.Count == 0) continue;

        // Only run when enough ticks have accumulated for this batch's sample rate
        if (ticksSinceLastUpdate < batch.SampleRate) continue;

        anyRan = true;
        float accumulatedTimeStep = timeStep * ticksSinceLastUpdate;

        var job = new SimplifiedMetabolismJob
        {
          TimeStep = accumulatedTimeStep,
          GasCount = manager.GasRegistry.GasCount,
          RegionStride = State.RegionStride,
          SentinelRegionIndex = State.SentinelRegionIndex,

          ToxWeights = batch.GlobalToxWeights,
          CausticWeights = batch.GlobalCausticWeights,
          BioInterferenceWeights = batch.GlobalBioInterferenceWeights,
          RadWeights = batch.GlobalRadWeights,
          VitalGasId = batch.VitalGasId,
          LethalThreshold = batch.LethalThreshold,
          SuffocationPenalty = batch.SuffocationPenalty,
          RecoveryRate = batch.RecoveryRate,

          RadiationSensitivity = batch.RadiationSensitivity,
          CausticSensitivity = batch.CausticSensitivity,

          MaxPressure = batch.MaxPressureKpa,
          MaxPressureFullDanger = batch.MaxPressureFullDangerKpa,
          MinPressure = batch.MinPressureKpa,
          MinPressureFullDanger = batch.MinPressureFullDangerKpa,

          VitalConsumptionRateUMol = batch.VitalConsumptionRateUMol,
          ByproductProductionRateUMol = batch.ByproductProductionRateUMol,
          ByproductGasId = batch.ByproductGasId,
          ExchangerPersistentIds = batch.ExchangerPersistentIds,
          GlobalIndexLookup = manager.ExchangerBuffer.GlobalIndexLookup,
          BufferDesiredFluxUMol = manager.ExchangerBuffer.DesiredFluxUMol,
          BufferInternalMicromoles = manager.ExchangerBuffer.InternalMicromoles,

          WorldToRegionIndex = State.GridLookups.WorldToRegionIndex,
          MolarFractions = State.RegionPhysicsBuffer.MolarFractions,
          PressureKpa = State.RegionGasComposition.PressureKpa,

          WorldIndices = batch.WorldIndices,
          ToxicResistances = batch.AnimalToxicResistance,
          CorrosiveResistances = batch.AnimalCorrosiveResistance,
          RadiationResistances = batch.AnimalRadiationResistance,
          PressureResistances = batch.AnimalPressureResistance,
          VacuumResistances = batch.AnimalVacuumResistance,

          Integrity = batch.AnimalIntegrity
        };
        var handle = job.Schedule(batch.Count, 32, dependency);
        combined = JobHandle.CombineDependencies(combined, handle);
      }

      if (anyRan) ticksSinceLastUpdate = 0;

      return AtmosphereManager.CompleteIfNeeded(combined);
    }
  }
}
