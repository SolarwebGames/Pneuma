namespace SolarWeb.Pneuma.Metabolism
{
  /// <summary>
  /// Defines a danger threshold for a specific gas in a metabolism.
  /// </summary>
  public struct GasDangerThreshold
  {
    // If current < MinFraction, danger starts increasing.
    // If current <= MinFullDangerFraction, danger is 1.0.
    public float MinFraction;
    public float MinFullDangerFraction;

    // If current > MaxFraction, danger starts increasing.
    // If current >= MaxFullDangerFraction, danger is 1.0.
    public float MaxFraction;
    public float MaxFullDangerFraction;

    public static GasDangerThreshold Safe() => new()
    {
      MinFraction = -1f,
      MinFullDangerFraction = -1f,
      MaxFraction = 2f, // Above 100%
      MaxFullDangerFraction = 2f
    };
  }
}
