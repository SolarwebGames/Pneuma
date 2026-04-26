using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace SolarWeb.Pneuma.Jobs.Diffusion
{
  [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
  public struct GatherActiveIndices : IJobParallelFor
  {
    [ReadOnly] public NativeArray<bool> IsActive;
    [ReadOnly] public NativeArray<int> ActiveTicks;
    public int DynamicRegionPoolStart;
    public int SentinelIndex;
    public NativeList<int>.ParallelWriter ActiveList;

    public void Execute(int index)
    {
      if (index == SentinelIndex) { ActiveList.AddNoResize(index); return; }
      if (!IsActive[index]) return;
      
      if (index >= DynamicRegionPoolStart && ActiveTicks[index] <= 0) return;
      ActiveList.AddNoResize(index);
    }
  }
}