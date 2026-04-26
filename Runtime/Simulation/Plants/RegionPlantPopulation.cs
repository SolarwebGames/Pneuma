using System;
using Unity.Collections;

namespace SolarWeb.Pneuma.Grid
{
  /// <summary>
  /// Tracks plant populations per unique (Profile, LeafRegion, RootRegion) combination.
  /// Uses Structure of Arrays (SoA) for better Burst/Job performance.
  /// </summary>
	public class RegionPlantPopulation : IDisposable
  {
    public NativeList<int> ProfileIdx;
    public NativeList<int> LeafRegionIdx;
    public NativeList<int> RootInRegionIdx;
    public NativeList<int> RootOutRegionIdx;
    public NativeList<int> CountMature;
    public NativeList<int> CountGrowing;
    public NativeList<int> CountSowing;

    public void Initialize(int regionStride, int profileCount)
    {
      int initialCapacity = regionStride * profileCount;
      ProfileIdx = new NativeList<int>(initialCapacity, Allocator.Persistent);
      LeafRegionIdx = new NativeList<int>(initialCapacity, Allocator.Persistent);
      RootInRegionIdx = new NativeList<int>(initialCapacity, Allocator.Persistent);
      RootOutRegionIdx = new NativeList<int>(initialCapacity, Allocator.Persistent);
      CountMature = new NativeList<int>(initialCapacity, Allocator.Persistent);
      CountGrowing = new NativeList<int>(initialCapacity, Allocator.Persistent);
      CountSowing = new NativeList<int>(initialCapacity, Allocator.Persistent);
    }

    public void Dispose()
    {
      if (ProfileIdx.IsCreated) ProfileIdx.Dispose();
      if (LeafRegionIdx.IsCreated) LeafRegionIdx.Dispose();
      if (RootInRegionIdx.IsCreated) RootInRegionIdx.Dispose();
      if (RootOutRegionIdx.IsCreated) RootOutRegionIdx.Dispose();
      if (CountMature.IsCreated) CountMature.Dispose();
      if (CountGrowing.IsCreated) CountGrowing.Dispose();
      if (CountSowing.IsCreated) CountSowing.Dispose();
    }
  }
}
