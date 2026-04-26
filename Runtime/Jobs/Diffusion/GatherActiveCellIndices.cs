using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace SolarWeb.Pneuma.Jobs.Diffusion
{
  [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
  public struct GatherActiveCellIndices : IJobParallelFor
  {
    [ReadOnly] public NativeArray<bool> IsActive;
    [ReadOnly] public NativeArray<int> SimToWorldIndex;
    [ReadOnly] public NativeArray<int> WorldToRegionIndex;
    public int SentinelRegionIndex;

    public NativeList<int>.ParallelWriter ActiveCellList;

    public void Execute(int simIdx)
    {
      int worldIdx = SimToWorldIndex[simIdx];
      if (worldIdx >= 0)
      {
        int regIdx = WorldToRegionIndex[worldIdx];
        if (regIdx >= 0 && regIdx != SentinelRegionIndex && IsActive[regIdx])
        {
          ActiveCellList.AddNoResize(simIdx);
        }
      }
    }
  }
}
