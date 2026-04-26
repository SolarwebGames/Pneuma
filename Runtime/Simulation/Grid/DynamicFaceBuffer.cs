using Unity.Collections;
using System;

namespace SolarWeb.Pneuma.Grid
{
  public class DynamicFaceBuffer : IDisposable
  {
    public NativeList<int> RegionA;
    public NativeList<int> RegionB;
    public NativeList<byte> Type;
    public NativeList<float> GasPermeability;
    public NativeList<float> ThermalConductivity;
    public NativeList<float> SurfaceArea;
    public NativeList<float> MinCollisionDiameter;
    public NativeList<float> MaxCollisionDiameter;
    public NativeList<float> MaxPressureDeltaKpa;

    public NativeList<float> LimitedVel;
    public NativeList<float> MaxSafeAdvection;
    public NativeList<float> TFactor;
    public NativeList<sbyte> FlowDirection;
    public NativeList<float> ActivePumpRate;
    public NativeList<float> MaxPumpPressureKpa;
    public NativeList<float> RegulatorKpa;
    public NativeList<float> WindExposureX;
    public NativeList<float> WindExposureZ;
    public NativeList<float> FacePressureOffsetKpa;

    public NativeList<float> GasFlux;
    public NativeList<float> TotalGasFlux;
    public NativeList<float> ThermalFlux;

    public NativeList<int> SourceEdge;

    public void Initialize(int initialCapacity, int gasCount)
    {
      RegionA = new NativeList<int>(initialCapacity, Allocator.Persistent);
      RegionB = new NativeList<int>(initialCapacity, Allocator.Persistent);
      Type = new NativeList<byte>(initialCapacity, Allocator.Persistent);
      GasPermeability = new NativeList<float>(initialCapacity, Allocator.Persistent);
      ThermalConductivity = new NativeList<float>(initialCapacity, Allocator.Persistent);
      SurfaceArea = new NativeList<float>(initialCapacity, Allocator.Persistent);
      MinCollisionDiameter = new NativeList<float>(initialCapacity, Allocator.Persistent);
      MaxCollisionDiameter = new NativeList<float>(initialCapacity, Allocator.Persistent);
      MaxPressureDeltaKpa = new NativeList<float>(initialCapacity, Allocator.Persistent);

      LimitedVel = new NativeList<float>(initialCapacity, Allocator.Persistent);
      MaxSafeAdvection = new NativeList<float>(initialCapacity, Allocator.Persistent);
      TFactor = new NativeList<float>(initialCapacity, Allocator.Persistent);
      FlowDirection = new NativeList<sbyte>(initialCapacity, Allocator.Persistent);
      ActivePumpRate = new NativeList<float>(initialCapacity, Allocator.Persistent);
      MaxPumpPressureKpa = new NativeList<float>(initialCapacity, Allocator.Persistent);
      RegulatorKpa = new NativeList<float>(initialCapacity, Allocator.Persistent);
      WindExposureX = new NativeList<float>(initialCapacity, Allocator.Persistent);
      WindExposureZ = new NativeList<float>(initialCapacity, Allocator.Persistent);
      FacePressureOffsetKpa = new NativeList<float>(initialCapacity, Allocator.Persistent);

      GasFlux = new NativeList<float>(initialCapacity * gasCount, Allocator.Persistent);
      TotalGasFlux = new NativeList<float>(initialCapacity, Allocator.Persistent);
      ThermalFlux = new NativeList<float>(initialCapacity, Allocator.Persistent);

      SourceEdge = new NativeList<int>(initialCapacity, Allocator.Persistent);
    }

    public void Dispose()
    {
      if (RegionA.IsCreated) RegionA.Dispose();
      if (RegionB.IsCreated) RegionB.Dispose();
      if (Type.IsCreated) Type.Dispose();
      if (GasPermeability.IsCreated) GasPermeability.Dispose();
      if (ThermalConductivity.IsCreated) ThermalConductivity.Dispose();
      if (SurfaceArea.IsCreated) SurfaceArea.Dispose();
      if (MinCollisionDiameter.IsCreated) MinCollisionDiameter.Dispose();
      if (MaxCollisionDiameter.IsCreated) MaxCollisionDiameter.Dispose();
      if (MaxPressureDeltaKpa.IsCreated) MaxPressureDeltaKpa.Dispose();

      if (LimitedVel.IsCreated) LimitedVel.Dispose();
      if (MaxSafeAdvection.IsCreated) MaxSafeAdvection.Dispose();
      if (TFactor.IsCreated) TFactor.Dispose();
      if (FlowDirection.IsCreated) FlowDirection.Dispose();
      if (ActivePumpRate.IsCreated) ActivePumpRate.Dispose();
      if (MaxPumpPressureKpa.IsCreated) MaxPumpPressureKpa.Dispose();
      if (RegulatorKpa.IsCreated) RegulatorKpa.Dispose();
      if (WindExposureX.IsCreated) WindExposureX.Dispose();
      if (WindExposureZ.IsCreated) WindExposureZ.Dispose();
      if (FacePressureOffsetKpa.IsCreated) FacePressureOffsetKpa.Dispose();

      if (GasFlux.IsCreated) GasFlux.Dispose();
      if (TotalGasFlux.IsCreated) TotalGasFlux.Dispose();
      if (ThermalFlux.IsCreated) ThermalFlux.Dispose();

      if (SourceEdge.IsCreated) SourceEdge.Dispose();
    }
  }
}
