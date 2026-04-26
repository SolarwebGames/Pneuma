using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace SolarWeb.Pneuma.Jobs.GasExchange
{
  [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
  public struct FindRegionBatches : IJob
  {
    [ReadOnly] public NativeArray<ulong> Keys;
    public int ExchangerCount;
    public int SentinelRegionIndex;
    
    public NativeList<int> BatchRegionIndices;
    public NativeList<int> BatchSampleRates;
    public NativeList<int> BatchStartIndices;
    public NativeList<int> BatchCounts;

    public void Execute()
    {
      BatchRegionIndices.Clear();
      BatchSampleRates.Clear();
      BatchStartIndices.Clear();
      BatchCounts.Clear();
      
      if (ExchangerCount == 0) return;

      int currentStart = 0;
      ulong currentKey = Keys[0];

      for (int i = 1; i <= ExchangerCount; i++)
      {
        bool isEnd = (i == ExchangerCount);
        bool isDifferent = false;
        
        ulong nextKey = 0;
        if (!isEnd)
        {
          nextKey = Keys[i];
          // We group batches by SampleRate and RegionIndex, ignoring GasId (which is the low 16 bits).
          // We mask out the lower 16 bits to check if the SampleRate or RegionIndex changed.
          isDifferent = ((nextKey & 0xFFFFFFFFFFFF0000) != (currentKey & 0xFFFFFFFFFFFF0000));
        }

        if (isEnd || isDifferent)
        {
          // ulong.MaxValue signifies inactive exchangers pushed to the end. Skip them.
          if (currentKey != ulong.MaxValue)
          {
            int sampleRate = (int)(currentKey >> 48);
            int regionIndex = (int)((currentKey >> 16) & 0xFFFFFFFF);
            int count = i - currentStart;

            // Parallelize Sentinel region processing.
            // Sentinel writes aren't propagated to the grid in UnifiedExchangerProcessor,
            // so concurrent threads processing Sentinel entities are safe.
            if (regionIndex == SentinelRegionIndex && count > 64)
            {
                int chunks = (int)math.ceil(count / 64f);
                for (int c = 0; c < chunks; c++)
                {
                    int chunkStart = currentStart + (c * 64);
                    int chunkCount = math.min(64, i - chunkStart);
                    
                    BatchRegionIndices.Add(regionIndex);
                    BatchSampleRates.Add(sampleRate);
                    BatchStartIndices.Add(chunkStart);
                    BatchCounts.Add(chunkCount);
                }
            }
            else
            {
                BatchRegionIndices.Add(regionIndex);
                BatchSampleRates.Add(sampleRate);
                BatchStartIndices.Add(currentStart);
                BatchCounts.Add(count);
            }
          }

          if (!isEnd)
          {
            currentStart = i;
            currentKey = nextKey;
          }
        }
      }
    }
  }
}
