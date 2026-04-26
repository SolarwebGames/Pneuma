using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace SolarWeb.Pneuma.Jobs.Combustion
{
  [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
  public struct DetectIgnition : IJobParallelForDefer
  {
    [ReadOnly] public NativeArray<int> ActiveRegionIndices;
    [ReadOnly] public NativeArray<long> uMoles;
    [ReadOnly] public NativeArray<long> TotalUMoles;
    [ReadOnly] public NativeArray<float> TemperatureK;
    [ReadOnly] public NativeArray<float> PressureKpa;
    [ReadOnly] public NativeArray<bool> IsBurning;

    [ReadOnly] public NativeArray<float> LowerExplosiveLimit;
    [ReadOnly] public NativeArray<float> UpperExplosiveLimit;
    [ReadOnly] public NativeArray<float> AutoIgnitionTemperature;
    [ReadOnly] public NativeArray<float> OxidizingPotency;

    public int GasCount;
    public int RegionStride;
    public float MinOxidizingPotency;
    public float MinCombustionPressureKpa;

    [WriteOnly] public NativeQueue<int>.ParallelWriter IgnitionEvents;

    public void Execute(int index)
    {
      if (index < 0 || index >= ActiveRegionIndices.Length) return;

      int rIdx = ActiveRegionIndices[index];

      if (rIdx < 0 || rIdx >= TotalUMoles.Length || rIdx >= TemperatureK.Length || rIdx >= IsBurning.Length) return;

      long totalUMoles = TotalUMoles[rIdx];

      if (totalUMoles <= 1000) return;

      if (PressureKpa[rIdx] < MinCombustionPressureKpa) return;

      double invTotal = 1.0 / (double)totalUMoles;

      double totalOxidizingPotency = 0;
      for (int g = 0; g < GasCount; g++)
      {
        if (g >= OxidizingPotency.Length) break;

        float potency = OxidizingPotency[g];
        if (potency > 0)
        {
          int dataIdx = (g * RegionStride) + rIdx;
          if (dataIdx >= 0 && dataIdx < uMoles.Length)
          {
            totalOxidizingPotency += ((double)uMoles[dataIdx] * invTotal) * (double)potency;
          }
        }
      }

      if (totalOxidizingPotency < (double)MinOxidizingPotency) return;

      float tempK = TemperatureK[rIdx];

      for (int g = 0; g < GasCount; g++)
      {
        if (g >= AutoIgnitionTemperature.Length || g >= OxidizingPotency.Length ||
            g >= LowerExplosiveLimit.Length || g >= UpperExplosiveLimit.Length) break;

        float ait = AutoIgnitionTemperature[g];
        if (ait <= 0f || OxidizingPotency[g] > 0.5f) continue;

        if (tempK >= ait)
        {
          int dataIdx = (g * RegionStride) + rIdx;
          if (dataIdx >= 0 && dataIdx < uMoles.Length)
          {
            double fuelFraction = (double)uMoles[dataIdx] * invTotal;
            double fuelPercentage = fuelFraction * 100.0;

            if (fuelPercentage >= (double)LowerExplosiveLimit[g] && fuelPercentage <= (double)UpperExplosiveLimit[g])
            {
              IgnitionEvents.Enqueue(rIdx);
              return;
            }
          }
        }
      }
    }
  }
}