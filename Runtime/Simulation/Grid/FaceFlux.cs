using System;
using SolarWeb.Pneuma.Data;
using Unity.Collections;

namespace SolarWeb.Pneuma.Grid
{
  public class FaceFlux : IDisposable
  {
    public NativeArray<float> FaceGasFlux;
    public NativeArray<float> FaceThermalFlux;
    public NativeArray<float> FaceConductiveFlux;    // conductive-only component of FaceThermalFlux
    public NativeArray<float> FaceEnergyFluxJoules;
    public NativeArray<float> FaceGasPermeability;
    public NativeArray<float> FacePressureOffsetKpa;
    public NativeArray<float> FaceLimitedVel;
    public NativeArray<float> FaceMaxSafeAdvection;

    public void Initialize(int faceStride, int gasCount)
    {
      FaceGasFlux.Resize(faceStride * gasCount);
      FaceThermalFlux.Resize(faceStride);
      FaceConductiveFlux.Resize(faceStride);
      FaceEnergyFluxJoules.Resize(faceStride);
      FaceGasPermeability.Resize(faceStride);
      FacePressureOffsetKpa.Resize(faceStride);
      FaceLimitedVel.Resize(faceStride);
      FaceMaxSafeAdvection.Resize(faceStride);
    }

    public void Dispose()
    {
      FaceGasFlux.SafeDispose();
      FaceThermalFlux.SafeDispose();
      FaceConductiveFlux.SafeDispose();
      FaceEnergyFluxJoules.SafeDispose();
      FaceGasPermeability.SafeDispose();
      FacePressureOffsetKpa.SafeDispose();
      FaceLimitedVel.SafeDispose();
      FaceMaxSafeAdvection.SafeDispose();
    }
  }
}