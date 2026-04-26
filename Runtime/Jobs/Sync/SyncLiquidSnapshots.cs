using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace SolarWeb.Pneuma.Jobs.Sync
{
  [BurstCompile]
  public struct SyncLiquidSnapshots : IJobParallelFor
  {
    [ReadOnly] public NativeArray<long> CurrentuMoles;
    [WriteOnly] public NativeArray<long> PreviousuMoles;

    public void Execute(int index)
    {
      PreviousuMoles[index] = CurrentuMoles[index];
    }
  }
}
