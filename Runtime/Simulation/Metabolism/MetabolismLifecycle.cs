using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

using SolarWeb.Pneuma.Data;
using SolarWeb.Pneuma.Jobs.Metabolism;
using SolarWeb.Pneuma.Metabolism;

namespace SolarWeb.Pneuma.Simulation
{
  public class MetabolismLifecycle
  {
    private readonly AtmosphereManager manager;

    public MetabolismLifecycle(AtmosphereManager manager)
    {
      this.manager = manager;
    }

    public void DoCheckMetabolismThresholds()
    {
      foreach (var batchInterface in manager.Metabolism.Batches.Values)
      {
        if (batchInterface.IsSimplified) continue;
        var batch = (MetabolismBatch)batchInterface;

        if (batch.SubscriptionCount == 0 || batch.Count == 0) continue;

        batch.StageJustTriggered.Clear();
        batch.StageJustReset.Clear();

        new CheckMetabolismThresholds
        {
          GasCount = batch.GasCount,
          GlobinCount = batch.GlobinCount,
          BlockSize = MetabolismBatch.BLOCK_SIZE,
          SubscriptionCount = batch.SubscriptionCount,
          PlasmaCapacityUMol = batch.Settings.PlasmaCapacityUMol,
          LungCapacityUMol = batch.Settings.LungCapacityUMol,
          PlasmaStorage = batch.PlasmaStorage,
          LungStorage = batch.LungStorage,
          GlobinLevels = batch.GlobinLevels,
          GasToGlobinIndex = batch.GasToGlobinIndex,
          GlobinCapacities = batch.GlobinCapacities,
          SubGasIds = batch.SubGasIds,
          SubThresholds = batch.SubThresholds,
          SubHysteresis = batch.SubHysteresis,
          SubIsExcess = batch.SubIsExcess,
          SubIsPlasma = batch.SubIsPlasma,
          StageActive = batch.StageActive,
          StageJustTriggered = batch.StageJustTriggered,
          StageJustReset = batch.StageJustReset,
        }.Schedule(batch.Count, 32).Complete();
      }
    }

    public void DoMetabolismCallbacks()
    {
      foreach (var batchInterface in manager.Metabolism.Batches.Values)
      {
        if (batchInterface.IsSimplified) continue;
        var batch = (MetabolismBatch)batchInterface;

        int batchCount = batch.Count;
        int subCount = batch.SubscriptionCount;
        if (subCount == 0 || batchCount == 0) continue;

        unsafe
        {
          bool* triggeredPtr = (bool*)batch.StageJustTriggered.GetUnsafeReadOnlyPtr();
          bool* resetPtr = (bool*)batch.StageJustReset.GetUnsafeReadOnlyPtr();

          for (int i = 0; i < batchCount; i++)
          {
            var metabolizer = batch.Metabolizers[i];
            if (metabolizer == null) continue;

            int stageBase = i * subCount;
            for (int s = 0; s < subCount; s++)
            {
              int idx = stageBase + s;
              if (triggeredPtr[idx]) metabolizer.OnStageTriggered(s);
              if (resetPtr[idx]) metabolizer.OnStageReset(s);
            }
          }
        }
      }
    }
  }
}
