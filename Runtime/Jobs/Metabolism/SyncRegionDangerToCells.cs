using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace SolarWeb.Pneuma.Jobs.Metabolism
{
  [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
  public struct SyncRegionDangerToCells : IJobParallelForDefer
  {
    [ReadOnly] public NativeArray<int> ActiveCellIndices;
    [ReadOnly] public NativeArray<int> SimToWorldIndex;
    [ReadOnly] public NativeArray<int> WorldToRegionIndex;

    [ReadOnly] public NativeArray<float> DangerLevels; // batchIdx * RegionStride + regIdx
    [ReadOnly] public NativeArray<float> PhysicalHazardLevels; // hazardIdx * RegionStride + regIdx

    [NativeDisableParallelForRestriction]
    public NativeArray<float> CellDangerLevels; // batchIdx * Stride + simIdx
    [NativeDisableParallelForRestriction]
    public NativeArray<float> CellPhysicalHazardLevels; // hazardIdx * Stride + simIdx

    public int BatchCount;
    public int Stride;
    public int RegionStride;
    public int SentinelRegionIndex;
    public int SentinelCellIndex;

    public void Execute(int index)
    {
      int simIdx = ActiveCellIndices[index];
      int worldIdx = SimToWorldIndex[simIdx];
      int regIdx = WorldToRegionIndex[worldIdx];

      // Robustness: ensure we don't access out of bounds regions
      if (regIdx < 0 || regIdx >= RegionStride) return;

      // 1. Sync Physical Hazards (same for all batches)
      for (int h = 0; h < 4; h++)
      {
        CellPhysicalHazardLevels[h * Stride + simIdx] = PhysicalHazardLevels[h * RegionStride + regIdx];
      }

      // 2. Sync Metabolism-Specific Danger Levels
      for (int b = 0; b < BatchCount; b++)
      {
        CellDangerLevels[b * Stride + simIdx] = DangerLevels[b * RegionStride + regIdx];
      }

      // 3. Explicitly Sync Sentinel Danger (External Environment)
      // This is necessary because the SentinelCellIndex is NOT in ActiveCellIndices.
      // We perform this check for every active cell to ensure the sentinel slot is eventually updated.
      // In a ParallelFor, this is safe but redundant; however, since SentinelCellIndex is global, 
      // it ensures that any world mapping to 'nothing' gets the correct ambient report.
      if (index == 0) // Only one thread needs to do this
      {
        for (int h = 0; h < 4; h++)
        {
          CellPhysicalHazardLevels[h * Stride + SentinelCellIndex] = PhysicalHazardLevels[h * RegionStride + SentinelRegionIndex];
        }
        for (int b = 0; b < BatchCount; b++)
        {
          CellDangerLevels[b * Stride + SentinelCellIndex] = DangerLevels[b * RegionStride + SentinelRegionIndex];
        }
      }
    }
  }
}
