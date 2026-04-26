using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace SolarWeb.Pneuma.Jobs.Sync
{
  /// <summary>
  /// Snapshots liquid + solid composition (Current → Previous) for condensable gases only.
  /// Non-condensable gas rows (AntoineA == 0) are always zero and need no copy.
  /// If no gases condense, executes instantly. Each condensable row is one MemCpy call.
  /// </summary>
  [BurstCompile]
  public unsafe struct CopyPhaseSnapshots : IJob
  {
    [ReadOnly] public NativeArray<long> LiquidSrc;
    [WriteOnly] public NativeArray<long> LiquidDst;
    [ReadOnly] public NativeArray<long> SolidSrc;
    [WriteOnly] public NativeArray<long> SolidDst;
    [ReadOnly] public NativeArray<int> CondensableGasIndices;
    public int CondensableGasCount;
    public int RegionStride;

    public void Execute()
    {
      if (CondensableGasCount == 0) return;

      long rowBytes = (long)RegionStride * UnsafeUtility.SizeOf<long>();
      long* liqSrc = (long*)LiquidSrc.GetUnsafeReadOnlyPtr();
      long* liqDst = (long*)LiquidDst.GetUnsafePtr();
      long* solSrc = (long*)SolidSrc.GetUnsafeReadOnlyPtr();
      long* solDst = (long*)SolidDst.GetUnsafePtr();

      for (int ci = 0; ci < CondensableGasCount; ci++)
      {
        long offset = (long)CondensableGasIndices[ci] * RegionStride;
        UnsafeUtility.MemCpy(liqDst + offset, liqSrc + offset, rowBytes);
        UnsafeUtility.MemCpy(solDst + offset, solSrc + offset, rowBytes);
      }
    }
  }
}
