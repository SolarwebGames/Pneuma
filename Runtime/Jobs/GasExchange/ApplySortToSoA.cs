using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace SolarWeb.Pneuma.Jobs.GasExchange
{
  [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
  public struct ApplySortToSoA : IJobParallelFor
  {
    [ReadOnly] public NativeArray<int> SortIndices;

    [ReadOnly] public NativeArray<ulong> OldKeys;
    [ReadOnly] public NativeArray<int> OldExchangerRegionIndices;
    [ReadOnly] public NativeArray<int> OldExchangerWorldIndices;
    [ReadOnly] public NativeArray<int> OldExchangerSimIndices;
    [ReadOnly] public NativeArray<int> OldExchangerSimIds;
    [ReadOnly] public NativeArray<int> OldGasIds;
    [ReadOnly] public NativeArray<long> OldDesiredFluxUMol;
    [ReadOnly] public NativeArray<long> OldActualFluxResultsUMol;
    [ReadOnly] public NativeArray<long> OldInternalMicromoles;
    [ReadOnly] public NativeArray<long> OldMaxInternalMicromoles;
    [ReadOnly] public NativeArray<bool> OldIsActive;
    [ReadOnly] public NativeArray<int> OldSampleRates;

    [WriteOnly] public NativeArray<ulong> NewKeys;
    [WriteOnly] public NativeArray<int> NewExchangerRegionIndices;
    [WriteOnly] public NativeArray<int> NewExchangerWorldIndices;
    [WriteOnly] public NativeArray<int> NewExchangerSimIndices;
    [WriteOnly] public NativeArray<int> NewExchangerSimIds;
    [WriteOnly] public NativeArray<int> NewGasIds;
    [WriteOnly] public NativeArray<long> NewDesiredFluxUMol;
    [WriteOnly] public NativeArray<long> NewActualFluxResultsUMol;
    [WriteOnly] public NativeArray<long> NewInternalMicromoles;
    [WriteOnly] public NativeArray<long> NewMaxInternalMicromoles;
    [WriteOnly] public NativeArray<bool> NewIsActive;
    [WriteOnly] public NativeArray<int> NewSampleRates;

    [NativeDisableParallelForRestriction]
    public NativeArray<int> GlobalIndexLookup;

    public void Execute(int newIdx)
    {
      int oldIdx = SortIndices[newIdx];

      NewKeys[newIdx] = OldKeys[oldIdx];
      NewExchangerRegionIndices[newIdx] = OldExchangerRegionIndices[oldIdx];
      NewExchangerWorldIndices[newIdx] = OldExchangerWorldIndices[oldIdx];
      NewExchangerSimIndices[newIdx] = OldExchangerSimIndices[oldIdx];
      
      int persistentId = OldExchangerSimIds[oldIdx];
      NewExchangerSimIds[newIdx] = persistentId;
      
      NewGasIds[newIdx] = OldGasIds[oldIdx];
      NewDesiredFluxUMol[newIdx] = OldDesiredFluxUMol[oldIdx];
      NewActualFluxResultsUMol[newIdx] = OldActualFluxResultsUMol[oldIdx];
      NewInternalMicromoles[newIdx] = OldInternalMicromoles[oldIdx];
      NewMaxInternalMicromoles[newIdx] = OldMaxInternalMicromoles[oldIdx];
      NewIsActive[newIdx] = OldIsActive[oldIdx];
      NewSampleRates[newIdx] = OldSampleRates[oldIdx];

      // Update the reverse lookup so external systems can find the new index
      if (persistentId >= 0 && persistentId < GlobalIndexLookup.Length)
      {
        GlobalIndexLookup[persistentId] = newIdx;
      }
    }
  }
}
