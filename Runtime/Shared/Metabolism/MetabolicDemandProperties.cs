namespace SolarWeb.Pneuma.Metabolism
{
  public struct MetabolicDemandProperties
  {
    public int GasId;
    public long RequiredUMolPerSecond;
    public float PenaltyMultiplier;
    public long CriticalThresholdBP; // 0-10000 (Basis Points)
  }
}