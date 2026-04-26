using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

using SolarWeb.Pneuma.Logging;
using SolarWeb.Pneuma.Metabolism;

namespace SolarWeb.Pneuma.Jobs.Metabolism
{
  [BurstCompile]
  public struct PlasmaFiltration : IJobParallelFor
  {
    public float TimeStep;
    public MetabolismProperties Props;
    public int GasCount;
    public int BlockSize;

    public NativeArray<long> PlasmaStorage;

    [ReadOnly] public NativeArray<long> ExcretionEfficiencies;
    [ReadOnly] public NativeArray<float> FiltrationEfficiencies;

    public void Execute(int i)
    {
      if (Props.PlasmaFiltrationRateUMol > 0)
      {
        int blockIdx = i / BlockSize;
        int laneIdx = i % BlockSize;
        int entityGasBase = blockIdx * GasCount * BlockSize + laneIdx;

        long baseFiltration = (long)(Props.PlasmaFiltrationRateUMol * TimeStep * FiltrationEfficiencies[i]);
        for (int g = 0; g < GasCount; g++)
        {
          int storageIdx = entityGasBase + (g * BlockSize);
          long efficiency = ExcretionEfficiencies[g];
          if (efficiency <= 0) continue;

          long toRemove = (baseFiltration * efficiency) / 10000;
          long actualRemoved = math.min(PlasmaStorage[storageIdx], toRemove);
          PlasmaStorage[storageIdx] -= actualRemoved;
        }
      }
    }
  }
}
