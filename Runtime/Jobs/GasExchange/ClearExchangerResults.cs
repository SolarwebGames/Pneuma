using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace SolarWeb.Pneuma.Jobs.GasExchange
{
  [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
  public struct ClearExchangerResults : IJob
  {
    public NativeArray<long> ActualFluxResultsUMol;

    public unsafe void Execute()
    {
      if (ActualFluxResultsUMol.Length > 0)
      {
        void* ptr = ActualFluxResultsUMol.GetUnsafePtr();
        long size = ActualFluxResultsUMol.Length * sizeof(long);
        UnsafeUtility.MemClear(ptr, size);
      }
    }
  }
}