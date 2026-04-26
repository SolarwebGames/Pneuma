using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace SolarWeb.Pneuma.Jobs.Sync
{
  [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
  public struct SyncAmbientToGrid : IJobParallelFor
  {
    [ReadOnly] public NativeArray<long> AmbientUMoles;
    [ReadOnly] public NativeArray<int> SimToWorldIndex;
    [ReadOnly] public NativeArray<int> WorldToSimIndex;
    [NativeDisableParallelForRestriction] public NativeArray<long> uMoles;

    public int GasCount;
    public int Stride;
    public int SentinelCellIndex;

    public void Execute(int gasId)
    {
      long val = AmbientUMoles[gasId];
      int gasOffset = gasId * Stride;
      uMoles[gasOffset + SentinelCellIndex] = val;
    }
  }
}
