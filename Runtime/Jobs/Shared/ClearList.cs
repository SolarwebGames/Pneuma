using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace SolarWeb.Pneuma.Jobs.Shared
{
  [BurstCompile]
  public struct ClearList : IJob
  {
    public NativeList<int> List;
    public void Execute() => List.Clear();
  }
}
