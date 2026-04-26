using Unity.Collections;
using System;

namespace SolarWeb.Pneuma.Regions
{
  public struct RegionGasData : IDisposable
  {
    // Stride-based indexing: [GasID * RegionStride + RegionIndex]
    public NativeArray<long> UMoles;
    public NativeArray<float> UMolesResidue;

    // Single-index: [RegionIndex]
    public NativeArray<long> TotalUMoles;
    public NativeArray<float> InvTotalUMoles;
    public NativeArray<float> Volume;
    public NativeArray<int> CellCount;

    public void Initialize(int regionStride, int gasCount, Allocator allocator)
    {
      UMoles = new NativeArray<long>(regionStride * gasCount, allocator);
      UMolesResidue = new NativeArray<float>(regionStride * gasCount, allocator);
      TotalUMoles = new NativeArray<long>(regionStride, allocator);
      InvTotalUMoles = new NativeArray<float>(regionStride, allocator);
      Volume = new NativeArray<float>(regionStride, allocator);
      CellCount = new NativeArray<int>(regionStride, allocator);
    }

    public void Dispose()
    {
      if (UMoles.IsCreated) UMoles.Dispose();
      if (UMolesResidue.IsCreated) UMolesResidue.Dispose();
      if (TotalUMoles.IsCreated) TotalUMoles.Dispose();
      if (InvTotalUMoles.IsCreated) InvTotalUMoles.Dispose();
      if (Volume.IsCreated) Volume.Dispose();
      if (CellCount.IsCreated) CellCount.Dispose();
    }
  }
}