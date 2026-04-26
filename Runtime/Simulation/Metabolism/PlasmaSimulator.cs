using SolarWeb.Pneuma.Jobs.Metabolism;
using SolarWeb.Pneuma.Logging;
using SolarWeb.Pneuma.Metabolism;
using Unity.Jobs;

namespace SolarWeb.Pneuma.Simulation
{
  public class PlasmaSimulator
  {
    private readonly AtmosphereManager manager;

    public PlasmaSimulator(AtmosphereManager manager)
    {
      this.manager = manager;
    }

    public JobHandle DoPlasmaDiffusion(float timeStep, JobHandle dependency = default)
    {
      JobHandle diffusionHandle = dependency;
      foreach (var batchInterface in manager.Metabolism.Batches.Values)
      {
        if (batchInterface.IsSimplified) continue;
        var batch = (MetabolismBatch)batchInterface;

        var jobHandle = new PlasmaDiffusion
        {
          TimeStep = timeStep,
          Props = batch.Settings,
          InvPlasmaCapacity = batch.InvPlasmaCapacity,
          BlockSize = MetabolismBatch.BLOCK_SIZE,
          Count = batch.Count,
          GasCount = batch.GasCount,
          GlobinCount = batch.GlobinCount,

          InternalStorage = batch.LungStorage,
          PlasmaStorage = batch.PlasmaStorage,
          GlobinLevels = batch.GlobinLevels,

          TotalFreePlasma = batch.TotalFreePlasma,
          BohrShifts = batch.BohrShifts,

          GlobinGasIds = batch.GlobinGasIds,
          GlobinGasGlobinIds = batch.GlobinGasGlobinIds,
          GlobinGasBindAffinities = batch.GlobinGasBindAffinities,
          GlobinCapacities = batch.GlobinCapacities,
          InvGlobinCapacities = batch.InvGlobinCapacities,
          GlobinHillCoeffs = batch.GlobinHillCoeffs,
          GlobinP50Fracs = batch.GlobinP50Fracs,
          GlobinGasLungUptakeFactors = batch.GlobinGasLungUptakeFactors,
          GlobinGasLungReleaseFactors = batch.GlobinGasLungReleaseFactors,
          FreeGasIds = batch.FreeGasIds,
          FreeGasUptakeFactors = batch.FreeGasUptakeFactors,
          FreeGasReleaseFactors = batch.FreeGasReleaseFactors,
          PlasmaExchangeEfficiencies = batch.EntityPlasmaExchangeEfficiency,
          EntitySolubilityScale = batch.EntitySolubilityScale,
        }.Schedule((batch.Count + MetabolismBatch.BLOCK_SIZE - 1) / MetabolismBatch.BLOCK_SIZE, 4, dependency);
        diffusionHandle = JobHandle.CombineDependencies(diffusionHandle, jobHandle);
      }

      return AtmosphereManager.CompleteIfNeeded(diffusionHandle);
    }

    public JobHandle DoMetabolicReactions(float timeStep, JobHandle dependency = default)
    {
      JobHandle metabolicReactions = dependency;
      foreach (var batchInterface in manager.Metabolism.Batches.Values)
      {
        if (batchInterface.IsSimplified) continue;
        var batch = (MetabolismBatch)batchInterface;

        var jobHandle = new MetabolicReactions
        {
          TimeStep = timeStep,
          GasCount = batch.GasCount,
          GlobinCount = batch.GlobinCount,
          BlockSize = MetabolismBatch.BLOCK_SIZE,
          Count = batch.Count,
          GlobinReactionCount = batch.GlobinReactionCount,
          PlasmaStorage = batch.PlasmaStorage,
          GlobinLevels = batch.GlobinLevels,
          Reactions = batch.Reactions,
          TotalFreePlasma = batch.TotalFreePlasma,
          Props = batch.Settings,
        }.Schedule((batch.Count + MetabolismBatch.BLOCK_SIZE - 1) / MetabolismBatch.BLOCK_SIZE, 4, dependency);

        metabolicReactions = JobHandle.CombineDependencies(metabolicReactions, jobHandle);
      }

      return AtmosphereManager.CompleteIfNeeded(metabolicReactions);
    }

    public JobHandle DoPlasmaFiltration(float timeStep, JobHandle dependency = default)
    {
      JobHandle plasmaFiltration = dependency;
      foreach (var batchInterface in manager.Metabolism.Batches.Values)
      {
        if (batchInterface.IsSimplified) continue;
        var batch = (MetabolismBatch)batchInterface;

        var jobHandle = new PlasmaFiltration
        {
          TimeStep = timeStep,
          Props = batch.Settings,
          GasCount = batch.GasCount,
          BlockSize = MetabolismBatch.BLOCK_SIZE,

          PlasmaStorage = batch.PlasmaStorage,
          FiltrationEfficiencies = batch.EntityPlasmaFiltrationEfficiency,
          ExcretionEfficiencies = batch.ExcretedEfficiencies,
        }.Schedule(batch.Count, 32, dependency);

        plasmaFiltration = JobHandle.CombineDependencies(plasmaFiltration, jobHandle);
      }

      return AtmosphereManager.CompleteIfNeeded(plasmaFiltration);
    }
  }
}
