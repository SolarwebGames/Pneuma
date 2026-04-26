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

    public NativeQueue<int> MergeQueue;
    public NativeQueue<int> PendingSplitWorldIndices;

    public void Initialize(int maxDynRegions)
    {
      Parent = new NativeArray<int>(maxDynRegions, Allocator.Persistent);
      WorldIdx = new NativeArray<int>(maxDynRegions, Allocator.Persistent);
      FaceStart = new NativeArray<int>(maxDynRegions, Allocator.Persistent);
      FaceCount = new NativeArray<int>(maxDynRegions, Allocator.Persistent);

      MergeQueue = new NativeQueue<int>(Allocator.Persistent);
      PendingSplitWorldIndices = new NativeQueue<int>(Allocator.Persistent);

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
