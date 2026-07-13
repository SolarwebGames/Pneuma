using Unity.Collections;
using System;

namespace SolarWeb.Pneuma.Grid
{
  public class DynamicRegionBuffer : IDisposable
  {
    public NativeArray<int> Parent;
    public NativeArray<int> WorldIdx;
    public NativeArray<int> FaceStart;
    public NativeArray<int> FaceCount;

    // Fixed-capacity lists rather than NativeQueue: written via ParallelWriter.AddNoResize from
    // Burst jobs, drained/Cleared once per tick on the main thread. See AtmosphereEvents.cs for why.
    public NativeList<int> MergeQueue;
    public NativeList<int> PendingSplitWorldIndices;

    public void Initialize(int maxDynRegions, int maxWorldCells)
    {
      Parent = new NativeArray<int>(maxDynRegions, Allocator.Persistent);
      WorldIdx = new NativeArray<int>(maxDynRegions, Allocator.Persistent);
      FaceStart = new NativeArray<int>(maxDynRegions, Allocator.Persistent);
      FaceCount = new NativeArray<int>(maxDynRegions, Allocator.Persistent);

      // At most one merge request per dynamic region slot.
      MergeQueue = new NativeList<int>(maxDynRegions, Allocator.Persistent);
      // Split requests are keyed by world cell index (from breach/bloom detection), so the true
      // worst case is one entry per world cell.
      PendingSplitWorldIndices = new NativeList<int>(maxWorldCells, Allocator.Persistent);

      for (int i = 0; i < maxDynRegions; i++)
      {
        Parent[i] = -1;
        WorldIdx[i] = -1;
      }
    }

    public void Dispose()
    {
      if (Parent.IsCreated) Parent.Dispose();
      if (WorldIdx.IsCreated) WorldIdx.Dispose();
      if (FaceStart.IsCreated) FaceStart.Dispose();
      if (FaceCount.IsCreated) FaceCount.Dispose();

      if (MergeQueue.IsCreated) MergeQueue.Dispose();
      if (PendingSplitWorldIndices.IsCreated) PendingSplitWorldIndices.Dispose();
    }
  }
}
