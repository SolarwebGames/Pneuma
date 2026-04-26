using System.Runtime.InteropServices;

namespace SolarWeb.Pneuma.Regions
{
  [StructLayout(LayoutKind.Sequential)]
  public struct RegionConnection
  {
    public int RegionA;
    public int RegionB;

    public float CrossSectionArea; // m^2 (Size of the door/vent)
    public float Conductivity;      // 0 to 1 (0 = closed door, 1 = open gap)

    public float FrictionLoss;
    public bool IsActive;
  }
}