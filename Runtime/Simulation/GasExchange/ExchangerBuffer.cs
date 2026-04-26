using SolarWeb.Pneuma.Data;
using System;
using System.Collections.Generic;
using Unity.Collections;

namespace SolarWeb.Pneuma.GasExchange
{

  public class ExchangerBuffer : IDisposable
  {
    // --- Main SoA Arrays (Double Buffered) ---
    public NativeArray<int> ExchangerRegionIndices;
    public NativeArray<int> ExchangerWorldIndices;
    public NativeArray<int> ExchangerSimIndices; // Unused but kept for interface compatibility if needed elsewhere
    public NativeArray<int> ExchangerSimIds;
    public NativeArray<int> GasIds;
    public NativeArray<long> DesiredFluxUMol;
    public NativeArray<long> ActualFluxResultsUMol;
    public NativeArray<long> InternalMicromoles;
    public NativeArray<long> MaxInternalMicromoles;
    public NativeArray<bool> IsActive;
    public NativeArray<int> SampleRates;

    // Buffers for physical reordering
    public NativeArray<int> ExchangerRegionIndices_B;
    public NativeArray<int> ExchangerWorldIndices_B;
    public NativeArray<int> ExchangerSimIndices_B;
    public NativeArray<int> ExchangerSimIds_B;
    public NativeArray<int> GasIds_B;
    public NativeArray<long> DesiredFluxUMol_B;
    public NativeArray<long> ActualFluxResultsUMol_B;
    public NativeArray<long> InternalMicromoles_B;
    public NativeArray<long> MaxInternalMicromoles_B;
    public NativeArray<bool> IsActive_B;
    public NativeArray<int> SampleRates_B;

    public NativeArray<int> GlobalIndexLookup;

    // --- Sorting Arrays (SoA) ---
    public NativeArray<ulong> SortKeys;
    public NativeArray<ulong> SortKeys_B;
    public NativeArray<int> SortIndices;

    // --- Region Batching (SoA) ---
    public NativeList<int> BatchRegionIndices;
    public NativeList<int> BatchSampleRates;
    public NativeList<int> BatchStartIndices;
    public NativeList<int> BatchCounts;

    public IExchanger?[] Exchangers;

    public int Count;
    public int Capacity;
    public bool IsDirty = true;
    public int NextPersistentId { get => nextPersistentId; set => nextPersistentId = value; }
    private int nextPersistentId = 0;

    // Tracks which sample rates > 1 are in use and how many exchangers use each
    public HashSet<int> ActiveSlowRates { get; } = new();
    private readonly Dictionary<int, int> slowRateCounts = new();

    public ExchangerBuffer(int initialCapacity = 128, int maxPossibleOwners = 10000)
    {
      Capacity = initialCapacity;

      AllocateSoA(ref ExchangerRegionIndices, Capacity);
      AllocateSoA(ref ExchangerWorldIndices, Capacity);
      AllocateSoA(ref ExchangerSimIndices, Capacity);
      AllocateSoA(ref ExchangerSimIds, Capacity);
      AllocateSoA(ref GasIds, Capacity);
      AllocateSoA(ref DesiredFluxUMol, Capacity);
      AllocateSoA(ref ActualFluxResultsUMol, Capacity);
      AllocateSoA(ref InternalMicromoles, Capacity);
      AllocateSoA(ref MaxInternalMicromoles, Capacity);
      AllocateSoA(ref IsActive, Capacity);
      AllocateSoA(ref SampleRates, Capacity);

      AllocateSoA(ref ExchangerRegionIndices_B, Capacity);
      AllocateSoA(ref ExchangerWorldIndices_B, Capacity);
      AllocateSoA(ref ExchangerSimIndices_B, Capacity);
      AllocateSoA(ref ExchangerSimIds_B, Capacity);
      AllocateSoA(ref GasIds_B, Capacity);
      AllocateSoA(ref DesiredFluxUMol_B, Capacity);
      AllocateSoA(ref ActualFluxResultsUMol_B, Capacity);
      AllocateSoA(ref InternalMicromoles_B, Capacity);
      AllocateSoA(ref MaxInternalMicromoles_B, Capacity);
      AllocateSoA(ref IsActive_B, Capacity);
      AllocateSoA(ref SampleRates_B, Capacity);

      SortKeys = new NativeArray<ulong>(Capacity, Allocator.Persistent);
      SortKeys_B = new NativeArray<ulong>(Capacity, Allocator.Persistent);
      SortIndices = new NativeArray<int>(Capacity, Allocator.Persistent);

      BatchRegionIndices = new NativeList<int>(Capacity, Allocator.Persistent);
      BatchSampleRates = new NativeList<int>(Capacity, Allocator.Persistent);
      BatchStartIndices = new NativeList<int>(Capacity, Allocator.Persistent);
      BatchCounts = new NativeList<int>(Capacity, Allocator.Persistent);

      GlobalIndexLookup.Resize(maxPossibleOwners);
      for (int i = 0; i < GlobalIndexLookup.Length; i++) GlobalIndexLookup[i] = -1;

      Exchangers = new IExchanger?[Capacity];
      Count = 0;
    }

    private void AllocateSoA<T>(ref NativeArray<T> array, int size) where T : struct
    {
      array = new NativeArray<T>(size, Allocator.Persistent);
    }

    public void ResizeRegionSortArrays(int regionStride)
    {
      // No longer used, but kept for interface compatibility if needed
    }

    public int Add(int gasId, long fluxUMol, long maxInternalUMol, IExchanger exchanger, int sampleRate = 1)
    {
      if (Count >= Capacity) Grow();

      int id = Count;
      int persistentId = nextPersistentId++;
      exchanger.Id = persistentId;

      ExchangerWorldIndices[id] = exchanger.Position;
      ExchangerRegionIndices[id] = exchanger.Region;
      GasIds[id] = gasId;
      DesiredFluxUMol[id] = fluxUMol;
      MaxInternalMicromoles[id] = maxInternalUMol;
      ExchangerSimIds[id] = persistentId;
      SampleRates[id] = sampleRate;

      InternalMicromoles[id] = 0L;
      ActualFluxResultsUMol[id] = 0L;
      IsActive[id] = true;
      Exchangers[id] = exchanger;

      GlobalIndexLookup[persistentId] = id;

      if (sampleRate > 1)
      {
        ActiveSlowRates.Add(sampleRate);
        slowRateCounts.TryGetValue(sampleRate, out int prev);
        slowRateCounts[sampleRate] = prev + 1;
      }

      Count++;
      IsDirty = true;
      return persistentId;
    }

    public void Remove(int persistentId)
    {
      int id = GlobalIndexLookup[persistentId];
      if (id < 0 || id >= Count) return;

      int removedRate = SampleRates[id];
      int lastIdx = Count - 1;

      if (id != lastIdx)
      {
        ExchangerWorldIndices[id] = ExchangerWorldIndices[lastIdx];
        ExchangerRegionIndices[id] = ExchangerRegionIndices[lastIdx];
        ExchangerSimIndices[id] = ExchangerSimIndices[lastIdx];
        GasIds[id] = GasIds[lastIdx];
        DesiredFluxUMol[id] = DesiredFluxUMol[lastIdx];
        ActualFluxResultsUMol[id] = ActualFluxResultsUMol[lastIdx];
        InternalMicromoles[id] = InternalMicromoles[lastIdx];
        MaxInternalMicromoles[id] = MaxInternalMicromoles[lastIdx];
        IsActive[id] = IsActive[lastIdx];
        SampleRates[id] = SampleRates[lastIdx];
        ExchangerSimIds[id] = ExchangerSimIds[lastIdx];
        Exchangers[id] = Exchangers[lastIdx];

        GlobalIndexLookup[ExchangerSimIds[id]] = id;
      }

      GlobalIndexLookup[persistentId] = -1;
      Exchangers[lastIdx] = null;
      IsActive[lastIdx] = false;
      Count--;

      if (removedRate > 1)
      {
        int remaining = slowRateCounts.TryGetValue(removedRate, out int prev) ? prev - 1 : 0;
        if (remaining <= 0)
        {
          slowRateCounts.Remove(removedRate);
          ActiveSlowRates.Remove(removedRate);
        }
        else
        {
          slowRateCounts[removedRate] = remaining;
        }
      }
      IsDirty = true;
    }

    public void MarkDirty(IExchanger exchanger) => IsDirty = true;

    public void SyncDirtyExchangers()
    {
      // In the new system, we rely on IsDirty being set when positions change.
      // If external systems modify IExchanger.Position, they should call UpdatePosition.
    }

    public void UpdatePosition(IExchanger exchanger)
    {
      int idx = GlobalIndexLookup[exchanger.Id]; // exchanger.Id is actually persistentId in external usage
      if (idx >= 0)
      {
        ExchangerWorldIndices[idx] = exchanger.Position;
        ExchangerRegionIndices[idx] = exchanger.Region;
        IsDirty = true;
      }
    }

    private void Grow()
    {
      int newCapacity = Capacity * 2;

      ExchangerRegionIndices.Resize(newCapacity);
      ExchangerWorldIndices.Resize(newCapacity);
      ExchangerSimIndices.Resize(newCapacity);
      ExchangerSimIds.Resize(newCapacity);
      GasIds.Resize(newCapacity);
      DesiredFluxUMol.Resize(newCapacity);
      ActualFluxResultsUMol.Resize(newCapacity);
      InternalMicromoles.Resize(newCapacity);
      MaxInternalMicromoles.Resize(newCapacity);
      IsActive.Resize(newCapacity);
      SampleRates.Resize(newCapacity);

      ExchangerRegionIndices_B.Resize(newCapacity);
      ExchangerWorldIndices_B.Resize(newCapacity);
      ExchangerSimIndices_B.Resize(newCapacity);
      ExchangerSimIds_B.Resize(newCapacity);
      GasIds_B.Resize(newCapacity);
      DesiredFluxUMol_B.Resize(newCapacity);
      ActualFluxResultsUMol_B.Resize(newCapacity);
      InternalMicromoles_B.Resize(newCapacity);
      MaxInternalMicromoles_B.Resize(newCapacity);
      IsActive_B.Resize(newCapacity);
      SampleRates_B.Resize(newCapacity);

      SortKeys.Resize(newCapacity);
      SortKeys_B.Resize(newCapacity);
      SortIndices.Resize(newCapacity);

      Array.Resize(ref Exchangers, newCapacity);
      Capacity = newCapacity;
      IsDirty = true;
    }

    public void Dispose()
    {
      ExchangerRegionIndices.SafeDispose();
      ExchangerWorldIndices.SafeDispose();
      ExchangerSimIndices.SafeDispose();
      ExchangerSimIds.SafeDispose();
      GasIds.SafeDispose();
      DesiredFluxUMol.SafeDispose();
      ActualFluxResultsUMol.SafeDispose();
      InternalMicromoles.SafeDispose();
      MaxInternalMicromoles.SafeDispose();
      IsActive.SafeDispose();
      SampleRates.SafeDispose();

      ExchangerRegionIndices_B.SafeDispose();
      ExchangerWorldIndices_B.SafeDispose();
      ExchangerSimIndices_B.SafeDispose();
      ExchangerSimIds_B.SafeDispose();
      GasIds_B.SafeDispose();
      DesiredFluxUMol_B.SafeDispose();
      ActualFluxResultsUMol_B.SafeDispose();
      InternalMicromoles_B.SafeDispose();
      MaxInternalMicromoles_B.SafeDispose();
      IsActive_B.SafeDispose();
      SampleRates_B.SafeDispose();

      SortKeys.SafeDispose();
      SortKeys_B.SafeDispose();
      SortIndices.SafeDispose();

      BatchRegionIndices.SafeDispose();
      BatchSampleRates.SafeDispose();
      BatchStartIndices.SafeDispose();
      BatchCounts.SafeDispose();

      GlobalIndexLookup.SafeDispose();
    }
  }
}