using Unity.Mathematics;

namespace SolarWeb.Pneuma.Data
{
  public struct StressPointData
  {
    public float3 Position;
    public float3 Normal;
    public float Intensity; // 0.0 to 1.0 (thresholded above 0.85)
  }

  public struct ThermalPointData
  {
    public float3 Position;
    public float Temperature;
  }
}
