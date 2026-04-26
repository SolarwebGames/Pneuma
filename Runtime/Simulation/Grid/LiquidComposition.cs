using System;
using SolarWeb.Pneuma.Data;
using Unity.Collections;

namespace SolarWeb.Pneuma.Grid
{
  /// <summary>
  /// Per-region per-gas liquid storage. Layout mirrors RegionGasComposition.uMoles:
  /// index = gasId * RegionStride + regionIdx.
  /// Only condensable gases (AntoineA != 0) will ever have non-zero values.
  /// </summary>
  public class LiquidComposition : IDisposable
  {
    /// <summary>Liquid amount [µmol]. Indexed as [gasId * RegionStride + regionIdx].</summary>
    public NativeArray<long> uMoles;

    public NativeArray<long> PendinguMolesDelta; // Atomic accumulation buffer

    /// <summary>Snapshot of liquid amount at start of tick. Used for conservative entrainment.</summary>
    public NativeArray<long> PreviousuMoles;

    public void Initialize(int gasCount, int regionStride)
    {
      uMoles.Resize(gasCount * regionStride);
      PendinguMolesDelta.Resize(gasCount * regionStride);
      PreviousuMoles.Resize(gasCount * regionStride);
    }

    public void Dispose()
    {
      uMoles.SafeDispose();
      PendinguMolesDelta.SafeDispose();
      PreviousuMoles.SafeDispose();
    }
  }
}
