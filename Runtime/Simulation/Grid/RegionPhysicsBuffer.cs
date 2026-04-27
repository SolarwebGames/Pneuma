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
  public NativeArray<float> EnclosingThermalCapacity;
  public NativeArray<float> EnclosingTemperatureK;
  public NativeArray<float> PreviousEnclosingTemperatureK;
  public NativeArray<float> EnclosingThermalConductance; // W/K, gas↔structure interface
  public NativeArray<float> EnclosingGasHeatFlux;        // transient, J per tick

  public NativeArray<float> InternalMassThermalCapacity;
  public NativeArray<float> InternalMassTemperatureK;
  public NativeArray<float> PreviousInternalMassTemperatureK;
  public NativeArray<float> InternalMassThermalConductance; // W/K, gas↔mass interface
  public NativeArray<float> InternalMassGasHeatFlux;        // transient, J per tick

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

    EnclosingThermalCapacity.Resize(stride);
    EnclosingTemperatureK.Resize(stride);
    PreviousEnclosingTemperatureK.Resize(stride);
    EnclosingThermalConductance.Resize(stride);
    EnclosingGasHeatFlux.Resize(stride);

    InternalMassThermalCapacity.Resize(stride);
    InternalMassTemperatureK.Resize(stride);
    PreviousInternalMassTemperatureK.Resize(stride);
    InternalMassThermalConductance.Resize(stride);
    InternalMassGasHeatFlux.Resize(stride);

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

    EnclosingThermalCapacity.SafeDispose();
    EnclosingTemperatureK.SafeDispose();
    PreviousEnclosingTemperatureK.SafeDispose();
    EnclosingThermalConductance.SafeDispose();
    EnclosingGasHeatFlux.SafeDispose();

    InternalMassThermalCapacity.SafeDispose();
    InternalMassTemperatureK.SafeDispose();
    PreviousInternalMassTemperatureK.SafeDispose();
    InternalMassThermalConductance.SafeDispose();
    InternalMassGasHeatFlux.SafeDispose();

    MaxPressureKpa.SafeDispose();
  }
}