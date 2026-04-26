using System.Threading;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

namespace SolarWeb.Pneuma.Jobs.Diffusion
{
  [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
  public unsafe struct AccumulateRegionDiffusionFlux : IJobParallelFor
  {
    [ReadOnly] public NativeArray<int> FaceRegionA, FaceRegionB;
    [ReadOnly] public NativeArray<float> FaceGasFlux;
    [ReadOnly] public NativeArray<float> FaceTotalGasFlux;
    [ReadOnly] public NativeArray<long> TotalUMoles;
    [ReadOnly] public NativeArray<long> PreviousLiquidUMoles;

    [NativeDisableParallelForRestriction] public NativeArray<long> PendingGasDelta;
    [NativeDisableParallelForRestriction] public NativeArray<long> PendingLiquidDelta;

    public int GasCount;
    public int RegionStride;
    public int FaceStride;
    public int FaceCount;
    public int SentinelIndex;

    public void Execute(int g)
    {
      int gasOffset = g * RegionStride;
      int fluxOffset = g * FaceStride;

      long* pGasDelta = (long*)PendingGasDelta.GetUnsafePtr();
      long* pLiqDelta = (long*)PendingLiquidDelta.GetUnsafePtr();

      for (int fIdx = 0; fIdx < FaceCount; fIdx++)
      {
        int iA = FaceRegionA[fIdx];
        int iB = FaceRegionB[fIdx];

        if (iA == SentinelIndex || iB == SentinelIndex) continue;

        float flux = FaceGasFlux[fluxOffset + fIdx];
        if (math.abs(flux) < 1e-6f) continue;

        long gasDeltaLong = (long)math.round(flux);
        if (gasDeltaLong != 0)
        {
          Interlocked.Add(ref pGasDelta[gasOffset + iA], -gasDeltaLong);
          Interlocked.Add(ref pGasDelta[gasOffset + iB], gasDeltaLong);
        }

        if (g == 0)
        {
          float totalFlux = FaceTotalGasFlux[fIdx];
          if (math.abs(totalFlux) > 1e-6f)
          {
            int srcIdx = (totalFlux > 0f) ? iA : iB;
            int dstIdx = (totalFlux > 0f) ? iB : iA;
            long srcTotal = TotalUMoles[srcIdx];

            if (srcTotal > 0)
            {
              double fracMoved = math.min(1.0, math.abs(totalFlux) / (double)srcTotal);
              for (int lg = 0; lg < GasCount; lg++)
              {
                long srcLiquid = PreviousLiquidUMoles[(lg * RegionStride) + srcIdx];
                if (srcLiquid > 0)
                {
                  long liqToMove = (long)math.round(srcLiquid * fracMoved);
                  if (liqToMove > 0)
                  {
                    Interlocked.Add(ref pLiqDelta[(lg * RegionStride) + srcIdx], -liqToMove);
                    Interlocked.Add(ref pLiqDelta[(lg * RegionStride) + dstIdx], liqToMove);
                  }
                }
              }
            }
          }
        }
      }
    }
  }
}