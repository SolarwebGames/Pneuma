using System;
using SolarWeb.Pneuma.Data;
using Unity.Collections;

namespace SolarWeb.Pneuma.Grid
{
  public class FacePhysics : IDisposable
  {
    public NativeArray<uint> FaceGasMask;
    public NativeArray<float> FaceSurfaceArea;
    public NativeArray<float> FaceTFactor;
    public NativeArray<float> FaceThermalConductivity;

    public void Initialize(int faceStride)
    {
      FaceGasMask.Resize(faceStride);
      FaceSurfaceArea.Resize(faceStride);
      FaceTFactor.Resize(faceStride);
      FaceThermalConductivity.Resize(faceStride);
    }

    public void Dispose()
    {
      FaceGasMask.SafeDispose();
      FaceSurfaceArea.SafeDispose();
      FaceTFactor.SafeDispose();
      FaceThermalConductivity.SafeDispose();
    }
  }
}