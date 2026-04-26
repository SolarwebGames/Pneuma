namespace SolarWeb.Pneuma.Metabolism
{
  /// <summary>
  /// Atmospheric danger criteria for a metabolism batch (non-gas factors).
  /// </summary>
  public struct MetabolismDangerCriteria
  {
    public float MinTemp, MaxTemp;
    public float MinTempFullDanger, MaxTempFullDanger;

    public float MinPressure, MaxPressure;
    public float MinPressureFullDanger, MaxPressureFullDanger;

    public float RadiationResistance; // 0 = standard, 1 = immune
    public float EnthalpySensitivity; // 1 = standard human-like steam sensitivity

    public float ReferenceBodyTempK;
    public float CausticResistance;
    public float RadiationSensitivity;
    public float CausticSensitivity;

    public static MetabolismDangerCriteria Default() => new MetabolismDangerCriteria
    {
      MinTemp = 0,
      MaxTemp = 1000,
      MinTempFullDanger = 0,
      MaxTempFullDanger = 1000,
      MinPressure = 0,
      MaxPressure = 1000,
      MinPressureFullDanger = 0,
      MaxPressureFullDanger = 1000,
      RadiationResistance = 0,
      EnthalpySensitivity = 1.0f,
      ReferenceBodyTempK = 310.15f,
      CausticResistance = 0,
      RadiationSensitivity = 2.0f,
      CausticSensitivity = 1.0f
    };
  }
}
