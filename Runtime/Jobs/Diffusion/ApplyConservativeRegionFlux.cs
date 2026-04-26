using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace SolarWeb.Pneuma.Jobs.Diffusion
{
  [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
  public struct ApplyConservativeRegionFlux : IJobParallelForDefer
  {
    [ReadOnly] public NativeArray<int> ActiveRegionIndices;

    [ReadOnly] public NativeArray<long> PendingGasDelta;
    [ReadOnly] public NativeArray<long> PendingLiquidDelta;
    [ReadOnly] public NativeArray<long> PendingSolidDelta;

    public NativeArray<long> uMoles;
    public NativeArray<long> LiquidUMoles;
    public NativeArray<long> SolidUMoles;
    public NativeArray<long> RegionNetFlux;
    public NativeArray<int> ActiveTicks;
    public int HysteresisTicks;

    public int GasCount;
    public int RegionStride;
    public int SentinelIndex;

    public void Execute(int index)
    {
      int simIdx = ActiveRegionIndices[index];
      if (simIdx == SentinelIndex) return;

      bool hasSignificantFlux = false;

      for (int g = 0; g < GasCount; g++)
      {
        int dataIdx = (g * RegionStride) + simIdx;

        long gasDelta = PendingGasDelta[dataIdx];
        if (gasDelta != 0)
        {
          RegionNetFlux[dataIdx] = gasDelta;
          uMoles[dataIdx] = math.max(0, uMoles[dataIdx] + gasDelta);
          hasSignificantFlux = true;
        }

        long liqDelta = PendingLiquidDelta[dataIdx];
        if (liqDelta != 0)
        {
          LiquidUMoles[dataIdx] = math.max(0, LiquidUMoles[dataIdx] + liqDelta);
          hasSignificantFlux = true;
        }

        long solDelta = PendingSolidDelta[dataIdx];
        if (solDelta != 0)
        {
          SolidUMoles[dataIdx] = math.max(0, SolidUMoles[dataIdx] + solDelta);
          hasSignificantFlux = true;
        }
      }

      if (hasSignificantFlux)
      {
        ActiveTicks[simIdx] = HysteresisTicks;
      }
    }
  }
}