using Unity.Mathematics;

namespace SolarWeb.Pneuma.Data
{
  public struct FaceLink
  {
    public int CellA;
    public int CellB;

    public StructuralProperties Props;
    public float SurfaceArea;
    public float PressureOffsetKpa;
    // 0=Wall, 1=Roof, 2=Floor
    public byte FaceType;
    public float MinCollisionDiameter;
    public float MaxCollisionDiameter;

    public int3 Direction;
    public sbyte FlowDirection;
    public float ActivePumpRate;

    public float WindExposureX;
    public float WindExposureZ;
  }
  }