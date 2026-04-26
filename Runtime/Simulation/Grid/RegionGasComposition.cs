using System;
using SolarWeb.Pneuma.Data;
using Unity.Collections;

namespace SolarWeb.Pneuma.Grid
{
  public class RegionGasComposition : IDisposable
  {
    public NativeArray<long> uMoles;
    public NativeArray<long> PendinguMolesDelta; // Atomic accumulation buffer
    public NativeArray<float> uMolesResidue;
    public NativeArray<long> RegionNetFlux; // Net flux per gas per region [GasCount * Stride]
    public NativeArray<long> TotalUMoles;
    public NativeArray<float> InvTotalUMoles;
    public NativeArray<float> PressureKpa;
    public NativeArray<float> PreviousPressureKpa;

    public void Initialize(int regionStride, int gasCount)
    {
      uMoles.Resize(regionStride * gasCount);
      PendinguMolesDelta.Resize(regionStride * gasCount);
      uMolesResidue.Resize(regionStride * gasCount);
      RegionNetFlux.Resize(regionStride * gasCount);

      TotalUMoles.Resize(regionStride);
      InvTotalUMoles.Resize(regionStride);
      PressureKpa.Resize(regionStride);
      PreviousPressureKpa.Resize(regionStride);
    }

    public void Dispose()
    {
      uMoles.SafeDispose();
      PendinguMolesDelta.SafeDispose();
      uMolesResidue.SafeDispose();
      RegionNetFlux.SafeDispose();
      TotalUMoles.SafeDispose();
      InvTotalUMoles.SafeDispose();
    }
  }
}