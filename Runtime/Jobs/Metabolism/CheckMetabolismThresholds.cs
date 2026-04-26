using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

using SolarWeb.Pneuma.Metabolism;

namespace SolarWeb.Pneuma.Jobs.Metabolism
{
  [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
  public struct CheckMetabolismThresholds : IJobParallelFor
  {
    public int GasCount;
    public int GlobinCount;
    public int BlockSize;
    public int SubscriptionCount;
    public long PlasmaCapacityUMol;
    public long LungCapacityUMol;

    [ReadOnly] public NativeArray<long> PlasmaStorage;
    [ReadOnly] public NativeArray<long> LungStorage;
    [ReadOnly] public NativeArray<GlobinState> GlobinLevels;
    [ReadOnly] public NativeArray<int> GasToGlobinIndex;
    [ReadOnly] public NativeArray<long> GlobinCapacities;

    // Per-batch subscription specs
    [ReadOnly] public NativeArray<int> SubGasIds;
    [ReadOnly] public NativeArray<float> SubThresholds;
    [ReadOnly] public NativeArray<float> SubHysteresis;
    [ReadOnly] public NativeArray<bool> SubIsExcess;
    [ReadOnly] public NativeArray<bool> SubIsPlasma;

    // Per-entity state
    public NativeArray<bool> StageActive;
    public NativeArray<bool> StageJustTriggered;
    public NativeArray<bool> StageJustReset;

    public void Execute(int entityIdx)
    {
      int blockIdx = entityIdx / BlockSize;
      int laneIdx = entityIdx % BlockSize;
      int entityGasBase = blockIdx * GasCount * BlockSize + laneIdx;
      int entityGlobinBase = blockIdx * GlobinCount * BlockSize + laneIdx;
      int stageBase = entityIdx * SubscriptionCount;

      for (int s = 0; s < SubscriptionCount; s++)
      {
        int gasId = SubGasIds[s];
        float threshold = SubThresholds[s];
        float hysteresis = SubHysteresis[s];
        bool isExcess = SubIsExcess[s];
        bool isPlasma = SubIsPlasma[s];

        float fraction;
        if (isPlasma)
        {
          int slot = GasToGlobinIndex[gasId];
          if (slot >= 0)
          {
            var globin = GlobinLevels[entityGlobinBase + (slot * BlockSize)];
            long occupied = globin.OccupantGasId == gasId ? globin.OccupiedUMol : 0L;
            long globinCap = GlobinCapacities[slot];
            fraction = globinCap > 0 ? (float)occupied / globinCap : 0f;
          }
          else
          {
            long level = PlasmaStorage[entityGasBase + (gasId * BlockSize)];
            fraction = PlasmaCapacityUMol > 0 ? (float)level / PlasmaCapacityUMol : 0f;
          }
        }
        else
        {
          long level = LungStorage[entityGasBase + (gasId * BlockSize)];
          fraction = LungCapacityUMol > 0 ? (float)level / LungCapacityUMol : 0f;
        }

        bool wasActive = StageActive[stageBase + s];
        bool isNowActive;

        if (!wasActive)
        {
          isNowActive = isExcess ? fraction >= threshold : fraction <= threshold;
        }
        else
        {
          bool reset = isExcess
            ? fraction < threshold - hysteresis
            : fraction > threshold + hysteresis;
          isNowActive = !reset;
        }

        StageJustTriggered[stageBase + s] = !wasActive && isNowActive;
        StageJustReset[stageBase + s] = wasActive && !isNowActive;
        StageActive[stageBase + s] = isNowActive;
      }
    }
  }
}
