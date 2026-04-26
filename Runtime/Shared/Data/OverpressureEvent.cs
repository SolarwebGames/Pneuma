namespace SolarWeb.Pneuma.Data
{
  public struct OverpressureEvent
  {
    public bool IsFaceEvent;
    public int Index; // RegionIndex if IsFaceEvent is false, FaceIndex if true
    public float PressureKpa;
  }
}
