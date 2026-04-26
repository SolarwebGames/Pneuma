using System.Collections.Generic;
using SolarWeb.Pneuma.Grid;
using SolarWeb.Pneuma.GasExchange;
using Unity.Collections;
using Unity.Jobs;
using SolarWeb.Pneuma.Jobs.GasExchange;

namespace SolarWeb.Pneuma.Simulation
{
  public class ExchangerSimulator
  {
    private AtmosphereGrid State => manager.State;
    private readonly AtmosphereManager manager;
    private int globalTick = 0;

    public ExchangerSimulator(AtmosphereManager manager)
    {
      this.manager = manager;
    }

    public JobHandle DoGasExchanges(float timeStep, JobHandle dependency = default)
    {
      var buf = manager.ExchangerBuffer;
      if (buf.Count == 0) return dependency;

      globalTick++;

      // 1. Prepare/Sort data if dirty
      JobHandle sortHandle = dependency;
      if (buf.IsDirty)
      {
        sortHandle = PerformSortAndReorder(dependency);
        buf.IsDirty = false;
      }

      // 2. Pre-clear results (on the now-sorted results array)
      var clearHandle = new ClearExchangerResults
      {
        ActualFluxResultsUMol = buf.ActualFluxResultsUMol,
      }.Schedule(sortHandle);

      // 3. Process batches grouped strictly by their SampleRate
      JobHandle combined = clearHandle;
      int batchCount = buf.BatchCounts.Length;

      int currentStart = -1;
      int currentRate = -1;

      for (int i = 0; i <= batchCount; i++)
      {
        bool isEnd = (i == batchCount);
        int rate = isEnd ? -1 : buf.BatchSampleRates[i];

        if (currentRate != rate || isEnd)
        {
          if (currentStart != -1)
          {
            // If the rate we just finished processing is active this tick
            if (globalTick % currentRate == 0)
            {
              float accumulatedTimeStep = timeStep * currentRate;
              int count = i - currentStart;
              combined = ScheduleProcessorSlice(currentStart, count, accumulatedTimeStep, combined);
            }
          }

          if (!isEnd)
          {
            currentStart = i;
            currentRate = rate;
          }
        }
      }

      return AtmosphereManager.CompleteIfNeeded(combined);
    }

    private JobHandle PerformSortAndReorder(JobHandle dependency)
    {
      var buf = manager.ExchangerBuffer;

      // A. Generate Keys and Indices
      var genHandle = new GenerateSortKeysJob
      {
        IsActive = buf.IsActive,
        ExchangerWorldIndices = buf.ExchangerWorldIndices,
        WorldToRegionIndex = State.GridLookups.WorldToRegionIndex,
        SampleRates = buf.SampleRates,
        GasIds = buf.GasIds,
        SentinelRegionIndex = State.SentinelRegionIndex,
        Keys = buf.SortKeys,
        Indices = buf.SortIndices
      }.Schedule(buf.Count, 64, dependency);

      // B. Sort Indices by Keys
      // Since NativeSortExtension.Sort(keys, values) is missing or ambiguous,
      // we sort the Indices array using a comparer that reads from Keys.
      genHandle.Complete();
      var indicesSlice = buf.SortIndices.GetSubArray(0, buf.Count);
      indicesSlice.Sort(new ExchangerSortComparer { Keys = buf.SortKeys });

      // C. Physically Reorder SoA
      var reorderHandle = new ApplySortToSoA
      {
        SortIndices = buf.SortIndices,

        OldKeys = buf.SortKeys,
        OldExchangerRegionIndices = buf.ExchangerRegionIndices,
        OldExchangerWorldIndices = buf.ExchangerWorldIndices,
        OldExchangerSimIndices = buf.ExchangerSimIndices,
        OldExchangerSimIds = buf.ExchangerSimIds,
        OldGasIds = buf.GasIds,
        OldDesiredFluxUMol = buf.DesiredFluxUMol,
        OldActualFluxResultsUMol = buf.ActualFluxResultsUMol,
        OldInternalMicromoles = buf.InternalMicromoles,
        OldMaxInternalMicromoles = buf.MaxInternalMicromoles,
        OldIsActive = buf.IsActive,
        OldSampleRates = buf.SampleRates,

        NewKeys = buf.SortKeys_B,
        NewExchangerRegionIndices = buf.ExchangerRegionIndices_B,
        NewExchangerWorldIndices = buf.ExchangerWorldIndices_B,
        NewExchangerSimIndices = buf.ExchangerSimIndices_B,
        NewExchangerSimIds = buf.ExchangerSimIds_B,
        NewGasIds = buf.GasIds_B,
        NewDesiredFluxUMol = buf.DesiredFluxUMol_B,
        NewActualFluxResultsUMol = buf.ActualFluxResultsUMol_B,
        NewInternalMicromoles = buf.InternalMicromoles_B,
        NewMaxInternalMicromoles = buf.MaxInternalMicromoles_B,
        NewIsActive = buf.IsActive_B,
        NewSampleRates = buf.SampleRates_B,

        GlobalIndexLookup = buf.GlobalIndexLookup
      }.Schedule(buf.Count, 64, default);

      // D. Find Batches
      var batchHandle = new FindRegionBatches
      {
        Keys = buf.SortKeys,
        ExchangerCount = buf.Count,
        SentinelRegionIndex = State.SentinelRegionIndex,
        BatchRegionIndices = buf.BatchRegionIndices,
        BatchSampleRates = buf.BatchSampleRates,
        BatchStartIndices = buf.BatchStartIndices,
        BatchCounts = buf.BatchCounts
      }.Schedule(reorderHandle);

      // E. Swap Buffers (Struct copy on main thread after completion or via a pointer swap if we had one)
      // Since we are scheduling, we need to ensure the swap happens AFTER reorderHandle completes.
      // However, we want to return a JobHandle. 
      // The cleanest way in a Job system without manual completion is to have the processor read from 
      // the B buffers if we just reordered, OR swap them right now and hope the jobs use the new pointers.
      // BUT Job structs capture the NativeArray at Schedule time. 
      // So if we swap the arrays in the 'buf' object now, the PREVIOUS jobs will still use the old ones,
      // and NEXT jobs (processor) will use the new ones.

      SwapBuffers(buf);

      return batchHandle;
    }

    private void SwapBuffers(ExchangerBuffer buf)
    {
      Swap(ref buf.SortKeys, ref buf.SortKeys_B);
      Swap(ref buf.ExchangerRegionIndices, ref buf.ExchangerRegionIndices_B);
      Swap(ref buf.ExchangerWorldIndices, ref buf.ExchangerWorldIndices_B);
      Swap(ref buf.ExchangerSimIndices, ref buf.ExchangerSimIndices_B);
      Swap(ref buf.ExchangerSimIds, ref buf.ExchangerSimIds_B);
      Swap(ref buf.GasIds, ref buf.GasIds_B);
      Swap(ref buf.DesiredFluxUMol, ref buf.DesiredFluxUMol_B);
      Swap(ref buf.ActualFluxResultsUMol, ref buf.ActualFluxResultsUMol_B);
      Swap(ref buf.InternalMicromoles, ref buf.InternalMicromoles_B);
      Swap(ref buf.MaxInternalMicromoles, ref buf.MaxInternalMicromoles_B);
      Swap(ref buf.IsActive, ref buf.IsActive_B);
      Swap(ref buf.SampleRates, ref buf.SampleRates_B);
    }

    private void Swap<T>(ref NativeArray<T> a, ref NativeArray<T> b) where T : struct => (a, b) = (b, a);

    public struct ExchangerSortComparer : IComparer<int>
    {
      public NativeArray<ulong> Keys;
      public int Compare(int x, int y) => Keys[x].CompareTo(Keys[y]);
    }

    private JobHandle ScheduleProcessorSlice(int startBatch, int batchCount, float timeStep, JobHandle dependency)
    {
      var buf = manager.ExchangerBuffer;

      // We create a slice of the batch arrays for this contiguous range
      return new UnifiedExchangerProcessor
      {
        BatchRegionIndices = buf.BatchRegionIndices.AsArray().GetSubArray(startBatch, batchCount),
        BatchStartIndices = buf.BatchStartIndices.AsArray().GetSubArray(startBatch, batchCount),
        BatchCounts = buf.BatchCounts.AsArray().GetSubArray(startBatch, batchCount),

        GasIds = buf.GasIds,
        IsActive = buf.IsActive,
        DesiredFluxUMol = buf.DesiredFluxUMol,
        MaxInternalMicromoles = buf.MaxInternalMicromoles,
        RegionPressureKpa = State.RegionGasComposition.PressureKpa,
        InternalMicromoles = buf.InternalMicromoles,
        ActualFluxResultsUMol = buf.ActualFluxResultsUMol,
        RegionUMoles = State.RegionGasComposition.uMoles,
        GasCount = State.GasCount,
        RegionStride = State.RegionStride,
        SentinelRegionIndex = State.SentinelRegionIndex,
        TimeStep = timeStep,
      }.Schedule(batchCount, 32, dependency);
    }

  }
}
