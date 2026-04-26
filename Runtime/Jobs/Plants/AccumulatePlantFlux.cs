using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace SolarWeb.Pneuma.Jobs.Plants
{
  [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
  public struct AccumulatePlantFlux : IJobParallelFor
  {
    public int GroupCount;
    public int SentinelRegionIndex;
    public int RegionStride;
    public int MaxReagentsPerProfile;

    [ReadOnly] public NativeArray<int> InputGasIds;
    [ReadOnly] public NativeArray<float> InputMolarRatios;
    [ReadOnly] public NativeArray<bool> InputIsRoot;
    [ReadOnly] public NativeArray<int> InputCounts;

    [ReadOnly] public NativeArray<int> OutputGasIds;
    [ReadOnly] public NativeArray<float> OutputMolarRatios;
    [ReadOnly] public NativeArray<bool> OutputIsRoot;
    [ReadOnly] public NativeArray<int> OutputCounts;

    [ReadOnly] public NativeArray<int> GroupProfileIdx;
    [ReadOnly] public NativeArray<int> GroupLeafRegionIdx;
    [ReadOnly] public NativeArray<int> GroupRootInRegionIdx;
    [ReadOnly] public NativeArray<int> GroupRootOutRegionIdx;

    [ReadOnly, DeallocateOnJobCompletion] public NativeArray<float> GroupFluxScale;

    // Parallel execution is over GasCount. Since each thread operates on a disjoint slice
    // of FluxAccumulator (defined by gasId * RegionStride), we don't need Interlocked operations!
    [NativeDisableParallelForRestriction] public NativeArray<long> FluxAccumulator;

    public void Execute(int gasId)
    {
      for (int i = 0; i < GroupCount; i++)
      {
        float fluxScale = GroupFluxScale[i];
        if (fluxScale <= 0f) continue;

        int pIdx = GroupProfileIdx[i];
        int leafReg = GroupLeafRegionIdx[i];
        if (leafReg < 0) leafReg = SentinelRegionIndex;
        int rootInReg = GroupRootInRegionIdx[i];
        int rootOutReg = GroupRootOutRegionIdx[i];

        int inCount = InputCounts[pIdx];
        for (int s = 0; s < inCount; s++)
        {
          int slot = pIdx * MaxReagentsPerProfile + s;
          if (InputGasIds[slot] == gasId)
          {
            bool isRoot = InputIsRoot[slot];
            if (isRoot && rootInReg < 0) continue;

            int targetReg = isRoot ? rootInReg : leafReg;
            long flux = (long)(fluxScale * InputMolarRatios[slot]);
            FluxAccumulator[gasId * RegionStride + targetReg] -= flux;
          }
        }

        int outCount = OutputCounts[pIdx];
        for (int s = 0; s < outCount; s++)
        {
          int slot = pIdx * MaxReagentsPerProfile + s;
          if (OutputGasIds[slot] == gasId)
          {
            bool isRoot = OutputIsRoot[slot];
            if (isRoot && rootOutReg < 0) continue;

            int targetReg = isRoot ? rootOutReg : leafReg;
            long flux = (long)(fluxScale * OutputMolarRatios[slot]);
            FluxAccumulator[gasId * RegionStride + targetReg] += flux;
          }
        }
      }
    }
  }
}
