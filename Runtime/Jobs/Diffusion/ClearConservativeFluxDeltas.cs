using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace SolarWeb.Pneuma.Jobs.Diffusion
{
  [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
  public struct ClearConservativeFluxDeltas : IJobParallelFor
  {
    public NativeArray<long> PendingGasDelta;
    public NativeArray<long> PendingLiquidDelta;
    public NativeArray<long> PendingSolidDelta;

    public void Execute(int index)
    {
      PendingGasDelta[index] = 0;
      if (index < PendingLiquidDelta.Length) PendingLiquidDelta[index] = 0;
      if (index < PendingSolidDelta.Length) PendingSolidDelta[index] = 0;
    }
  }
}
