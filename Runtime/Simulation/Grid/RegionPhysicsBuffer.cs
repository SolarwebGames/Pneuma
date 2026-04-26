using SolarWeb.Pneuma.Data;
using System;
using Unity.Collections;

public class RegionPhysicsBuffer : IDisposable
{
  public NativeArray<float> RegionVolumes;
  public NativeArray<float> TemperatureK;
  public NativeArray<float> PreviousTemperatureK;
  public NativeArray<float> RoomCurrentTemperatureK;
  public NativeArray<float> AverageMolarMass;
  public NativeArray<float> MolarFractions; // (GasCount * RegionStride)
  public NativeArray<float> SpeedOfSound;
  public NativeArray<float> InvVolConst;
  public NativeArray<float> TFactor;
  public NativeArray<float> MixtureMolarCp;
  public NativeArray<float> StructuralThermalCapacity;
  public NativeArray<float> StructuralTemperatureK;
  public NativeArray<float> PreviousStructuralTemperatureK;
  public NativeArray<float> StructuralTemperatureResidueK;
  public NativeArray<float> StructuralThermalConductance; // W/K, gas↔structure interface
  public NativeArray<float> StructuralGasHeatFlux;        // transient, J per tick
  public NativeArray<float> MaxPressureKpa;

  public NativeArray<bool> IsHighGradient;

  public void Initialize(int gasCount, int stride)
  {
    RegionVolumes.Resize(stride);
    TemperatureK.Resize(stride);
    PreviousTemperatureK.Resize(stride);
    RoomCurrentTemperatureK.Resize(stride);
    AverageMolarMass.Resize(stride);
    MolarFractions.Resize(stride * gasCount);
    IsHighGradient.Resize(stride);
    SpeedOfSound.Resize(stride);
    InvVolConst.Resize(stride);
    TFactor.Resize(stride);
    MixtureMolarCp.Resize(stride);
    StructuralThermalCapacity.Resize(stride);
    StructuralTemperatureK.Resize(stride);
    PreviousStructuralTemperatureK.Resize(stride);
    StructuralTemperatureResidueK.Resize(stride);
    StructuralThermalConductance.Resize(stride);
    StructuralGasHeatFlux.Resize(stride);
    MaxPressureKpa.Resize(stride);
  }

  public void Dispose()
  {
    RegionVolumes.SafeDispose();
    TemperatureK.SafeDispose();
    PreviousTemperatureK.SafeDispose();
    RoomCurrentTemperatureK.SafeDispose();
    AverageMolarMass.SafeDispose();
    MolarFractions.SafeDispose();
    IsHighGradient.SafeDispose();
    SpeedOfSound.SafeDispose();
    InvVolConst.SafeDispose();
    TFactor.SafeDispose();
    MixtureMolarCp.SafeDispose();
    StructuralThermalCapacity.SafeDispose();
    StructuralTemperatureK.SafeDispose();
    PreviousStructuralTemperatureK.SafeDispose();
    StructuralTemperatureResidueK.SafeDispose();
    StructuralThermalConductance.SafeDispose();
    StructuralGasHeatFlux.SafeDispose();
    MaxPressureKpa.SafeDispose();
  }
}