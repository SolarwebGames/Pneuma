using SolarWeb.Pneuma.Data;
using System;
using Unity.Collections;

public class WorldPhysicsBuffer : IDisposable
{
  public NativeArray<float> CellVolumes;
  public NativeArray<float> WindExposure;

  public void Initialize(int worldCellCount)
  {
    CellVolumes.Resize(worldCellCount);
    WindExposure.Resize(worldCellCount);
  }

  public void Dispose()
  {
    CellVolumes.Dispose();
    WindExposure.SafeDispose();
  }
}