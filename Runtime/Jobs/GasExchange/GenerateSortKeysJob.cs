using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace SolarWeb.Pneuma.Jobs.GasExchange
{
  [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
  public struct GenerateSortKeysJob : IJobParallelFor
  {
    [ReadOnly] public NativeArray<bool> IsActive;
    [ReadOnly] public NativeArray<int> ExchangerWorldIndices;
    [ReadOnly] public NativeArray<int> WorldToRegionIndex;
    [ReadOnly] public NativeArray<int> SampleRates;
    [ReadOnly] public NativeArray<int> GasIds;

    public int SentinelRegionIndex;

    [WriteOnly] public NativeArray<ulong> Keys;
    [WriteOnly] public NativeArray<int> Indices;

    public void Execute(int i)
    {
      if (!IsActive[i])
      {
        Keys[i] = ulong.MaxValue;
        Indices[i] = i;
        return;
      }

      int worldIdx = ExchangerWorldIndices[i];
      int regIdx = SentinelRegionIndex;

      if (worldIdx >= 0 && worldIdx < WorldToRegionIndex.Length)
      {
        regIdx = WorldToRegionIndex[worldIdx];
        if (regIdx < 0) regIdx = SentinelRegionIndex;
      }

      int sampleRate = SampleRates[i];
      int gasId = GasIds[i];

      // Pack sample rate into high 16 bits, region index into mid 32 bits, gas id into low 16 bits
      // We cast regionIndex and gasId to uint so negative sentinel regions sort properly or at least consistently
      Keys[i] = ((ulong)(uint)sampleRate << 48) | ((ulong)(uint)regIdx << 16) | (ushort)gasId;
      Indices[i] = i;
    }
  }
}
