using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace SolarWeb.Pneuma.Jobs.GasExchange
{
  /// <summary>
  /// Processes all gas exchangers in parallel, one thread per region batch.
  ///
  /// Each thread owns a single region batch and serially visits every exchanger
  /// registered there. Because exchangers are pre-sorted by [SampleRate | RegionIndex | GasId],
  /// we can process contiguous blocks of the same gas type without needing to iterate
  /// over all possible gases in the simulation, vastly improving performance and SIMD vectorization.
  /// </summary>
  [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
  public struct UnifiedExchangerProcessor : IJobParallelFor
  {
    [ReadOnly] public NativeArray<int> BatchRegionIndices;
    [ReadOnly] public NativeArray<int> BatchStartIndices;
    [ReadOnly] public NativeArray<int> BatchCounts;

    // Exchanger data (read) - Physically reordered SoA
    [ReadOnly] public NativeArray<int> GasIds;
    [ReadOnly] public NativeArray<bool> IsActive;
    [ReadOnly] public NativeArray<long> DesiredFluxUMol;
    [ReadOnly] public NativeArray<long> MaxInternalMicromoles;
    [ReadOnly] public NativeArray<float> RegionPressureKpa;

    // Exchanger data (write) — each exIdx is owned by exactly one batch, no races
    public NativeArray<long> InternalMicromoles;
    public NativeArray<long> ActualFluxResultsUMol;

    // Grid write — each thread owns its own static/dynamic region uniquely
    public NativeArray<long> RegionUMoles;

    public int GasCount;
    public int RegionStride;
    public int SentinelRegionIndex;
    public float TimeStep;

    public unsafe void Execute(int i)
    {
      int regIdx = BatchRegionIndices[i];
      int start = BatchStartIndices[i];
      int count = BatchCounts[i];

      if (count == 0) return;

      float pressure = RegionPressureKpa[regIdx];
      bool isSentinel = (regIdx < 0 || regIdx == SentinelRegionIndex);

      int currentGasId = -1;
      long gasAvailable = 0;
      long gasFluxSum = 0;

      for (int j = 0; j <= count; j++)
      {
        bool isEnd = (j == count);
        int exIdx = isEnd ? -1 : start + j;
        int nextGasId = isEnd ? -1 : GasIds[exIdx];

        if (currentGasId != nextGasId || isEnd)
        {
          // 1. Flush previous gas changes to the grid (if not sentinel and flux occurred)
          if (currentGasId >= 0 && gasFluxSum != 0 && !isSentinel)
          {
            RegionUMoles[(currentGasId * RegionStride) + regIdx] += gasFluxSum;
          }

          if (isEnd) break;

          // 2. Load next gas state
          currentGasId = nextGasId;
          gasFluxSum = 0;
          gasAvailable = isSentinel ? long.MaxValue : RegionUMoles[(currentGasId * RegionStride) + regIdx];
        }

        if (!IsActive[exIdx])
        {
          ActualFluxResultsUMol[exIdx] = 0;
          continue;
        }

        long desiredFluxTickUMol = (long)math.round(DesiredFluxUMol[exIdx] * (double)TimeStep);

        if (desiredFluxTickUMol > 0) // exhale: entity → grid
        {
          if (pressure > 500f)
          {
            ActualFluxResultsUMol[exIdx] = 0;
            continue;
          }

          long toMove = math.min(desiredFluxTickUMol, InternalMicromoles[exIdx]);
          if (toMove >= 1L)
          {
            InternalMicromoles[exIdx] -= toMove;
            ActualFluxResultsUMol[exIdx] = toMove;
            gasFluxSum += toMove;
          }
          else
          {
            ActualFluxResultsUMol[exIdx] = 0;
          }
        }
        else if (desiredFluxTickUMol < 0) // inhale: grid → entity
        {
          long absRequest = -desiredFluxTickUMol;
          long toMove = math.min(absRequest, gasAvailable);

          if (toMove > 0)
          {
            gasAvailable -= toMove; // Consume from local snapshot
            InternalMicromoles[exIdx] += toMove;
            ActualFluxResultsUMol[exIdx] = -toMove;
            gasFluxSum -= toMove;
          }
          else
          {
            ActualFluxResultsUMol[exIdx] = 0;
          }
        }
        else
        {
          ActualFluxResultsUMol[exIdx] = 0;
        }
      }
    }
  }
}
