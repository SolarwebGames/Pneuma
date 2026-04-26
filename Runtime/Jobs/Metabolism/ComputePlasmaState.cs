using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace SolarWeb.Pneuma.Jobs.Metabolism
{
  [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
  public struct ComputePlasmaState : IJobParallelFor
  {
    public int GasCount;
    public int BlockSize;
    public int Count;
    public float InvPlasmaCapacity;
    public float AllostericSensitivity;

    [ReadOnly] public NativeArray<long> PlasmaStorage;
    [ReadOnly] public NativeArray<float> GasStructuralWarpingPotentials;

    public NativeArray<long> TotalFreePlasma;
    public NativeArray<float> AllostericSums;
    public NativeArray<float> BohrShifts;

    public void Execute(int blockIdx)
    {
      int blockBase = blockIdx * GasCount * BlockSize;
      int entityBase = blockIdx * BlockSize;

      // The use of long prevents auto-vectorization of this loop. If this becomes a bottleneck, we can switch the array to int, which has a max value that should still be sufficient for pawn plasma storage.
      for (int lane = 0; lane < BlockSize; lane++)
      {
        if (entityBase + lane >= Count) continue;

        long tfp = 0;
        float sum = 0f;

        for (int g = 0; g < GasCount; g++)
        {
          int gasBase = blockBase + (g * BlockSize);
          float swp = GasStructuralWarpingPotentials[g];

          long amt = PlasmaStorage[gasBase + lane];
          tfp += amt;
          sum += (float)amt * InvPlasmaCapacity * swp;
        }

        TotalFreePlasma[entityBase + lane] = tfp;
        AllostericSums[entityBase + lane] = sum;
        BohrShifts[entityBase + lane] = 1f + (sum * AllostericSensitivity);
      }
    }
  }
}
