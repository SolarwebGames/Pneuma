using SolarWeb.Pneuma.Logging;
using SolarWeb.Pneuma.Grid;
using SolarWeb.Pneuma.Metabolism;
using Unity.Jobs;
using SolarWeb.Pneuma.Jobs.Metabolism;

namespace SolarWeb.Pneuma.Simulation
{
  public class RespiratorySimulator
  {
    private AtmosphereGrid State => manager.State;
    private readonly AtmosphereManager manager;

    public RespiratorySimulator(AtmosphereManager manager)
    {
      this.manager = manager;
    }

    public JobHandle DoBreathe(float timeStep, JobHandle dependency = default)
    {
      JobHandle combined = dependency;

      foreach (var batchInterface in manager.Metabolism.Batches.Values)
      {
        if (batchInterface.IsSimplified) continue;
        var batch = (MetabolismBatch)batchInterface;

        MetabolismProviderManager.DoExternalProviderPreStep(batch);

        int blockCount = (batch.Count + MetabolismBatch.BLOCK_SIZE - 1) / MetabolismBatch.BLOCK_SIZE;

        var breatheHandle = new Breathe
        {
          TimeStep = timeStep,
          Props = batch.Settings,
          GasCount = batch.GasCount,
          BlockSize = MetabolismBatch.BLOCK_SIZE,
          Count = batch.Count,
          RegionStride = State.RegionStride,
          SentinelRegionIndex = State.SentinelRegionIndex,
          WorldIndices = batch.WorldIndices,
          WorldToRegionIndex = State.GridLookups.WorldToRegionIndex,
          LungEfficiencies = batch.EntityLungEfficiency,
          RegionUMoles = State.RegionGasComposition.uMoles,
          RegionTotalUMoles = State.RegionGasComposition.TotalUMoles,
          RegionPressureKpa = State.RegionGasComposition.PressureKpa,
          InternalStorage = batch.LungStorage,
          EntityLungNetFlux = batch.EntityLungNetFlux,
          HasExternalSupply = batch.HasExternalSupply,
          ExternalLungSupply = batch.ExternalLungSupply,
          ExternalNetConsumed = batch.ExternalNetConsumed,
        }.Schedule(blockCount, 4, dependency);

        var fluxHandle = new ApplyLungFlux
        {
          Count = batch.Count,
          GasCount = batch.GasCount,
          BlockSize = MetabolismBatch.BLOCK_SIZE,
          RegionStride = State.RegionStride,
          SentinelRegionIndex = State.SentinelRegionIndex,
          WorldIndices = batch.WorldIndices,
          WorldToRegionIndex = State.GridLookups.WorldToRegionIndex,
          EntityLungNetFlux = batch.EntityLungNetFlux,
          RegionUMoles = State.RegionGasComposition.uMoles,
        }.Schedule(breatheHandle);

        combined = JobHandle.CombineDependencies(combined, fluxHandle);
      }

      return AtmosphereManager.CompleteIfNeeded(combined);
    }

    public void DoExternalProviderPostStep()
    {
      foreach (var batchInterface in manager.Metabolism.Batches.Values)
      {
        if (batchInterface.IsSimplified) continue;
        var batch = (MetabolismBatch)batchInterface;
        MetabolismProviderManager.DoExternalProviderPostStep(batch);
      }
    }

    public JobHandle DoPrecompute(JobHandle dependency = default)
    {
      JobHandle precomputeHandle = dependency;
      foreach (var batchInterface in manager.Metabolism.Batches.Values)
      {
        if (batchInterface.IsSimplified) continue;
        var batch = (MetabolismBatch)batchInterface;

        var solubilityJob = new PrecomputeMetabolism
        {
          WorldIndices = batch.WorldIndices,
          WorldToRegionIndex = State.GridLookups.WorldToRegionIndex,
          RegionTemperatureK = State.RegionPhysicsBuffer.TemperatureK,
          BodyTemperature_K = batch.Settings.BodyTemperature,
          EntitySolubilityScale = batch.EntitySolubilityScale,
        }.Schedule(batch.Count, 32, precomputeHandle);

        var plasmaStateJob = new ComputePlasmaState
        {
          GasCount = batch.GasCount,
          BlockSize = MetabolismBatch.BLOCK_SIZE,
          Count = batch.Count,
          InvPlasmaCapacity = batch.InvPlasmaCapacity,
          AllostericSensitivity = batch.Settings.AllostericSensitivity,
          PlasmaStorage = batch.PlasmaStorage,
          GasStructuralWarpingPotentials = batch.GasStructuralWarpingPotentials,
          TotalFreePlasma = batch.TotalFreePlasma,
          AllostericSums = batch.AllostericSums,
          BohrShifts = batch.BohrShifts,
        }.Schedule((batch.Count + MetabolismBatch.BLOCK_SIZE - 1) / MetabolismBatch.BLOCK_SIZE, 4, precomputeHandle);

        precomputeHandle = JobHandle.CombineDependencies(solubilityJob, plasmaStateJob);
      }

      return AtmosphereManager.CompleteIfNeeded(precomputeHandle);
    }
  }
}
