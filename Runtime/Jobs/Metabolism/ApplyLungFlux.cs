using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace SolarWeb.Pneuma.Jobs.Metabolism
{
  /// <summary>
  /// Single-threaded job that drains EntityLungNetFlux (written by Breathe) into RegionUMoles.
  /// net > 0 (exhale): gas leaves lung → enters region.
  /// net &lt; 0 (inhale): gas enters lung ← leaves region.
  /// Both handled by: RegionUMoles += EntityLungNetFlux (positive adds, negative subtracts).
  /// Sentinel entities are skipped (their EntityLungNetFlux is 0, but skip for safety).
  /// </summary>
  [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
  public struct ApplyLungFlux : IJob
  {
    public int Count;
    public int GasCount;
    public int BlockSize;
    public int RegionStride;
    public int SentinelRegionIndex;

    [ReadOnly] public NativeArray<int>  WorldIndices;
    [ReadOnly] public NativeArray<int>  WorldToRegionIndex;
    [ReadOnly] public NativeArray<long> EntityLungNetFlux;

    public NativeArray<long> RegionUMoles;

    public void Execute()
    {
      for (int e = 0; e < Count; e++)
      {
        int worldIdx = WorldIndices[e];
        int regIdx = (worldIdx >= 0 && worldIdx < WorldToRegionIndex.Length)
            ? WorldToRegionIndex[worldIdx] : -1;
        if (regIdx < 0 || regIdx == SentinelRegionIndex) continue;

        int blockIdx = e / BlockSize;
        int laneIdx  = e % BlockSize;
        int blockBase = blockIdx * GasCount * BlockSize;

        for (int g = 0; g < GasCount; g++)
        {
          int idx = blockBase + g * BlockSize + laneIdx;
          RegionUMoles[g * RegionStride + regIdx] += EntityLungNetFlux[idx];
        }
      }
    }
  }
}
