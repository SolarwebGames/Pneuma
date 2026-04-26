namespace SolarWeb.Pneuma.Metabolism
{
  public struct MetabolismProperties
  {
    /// <summary>Continuous ventilation rate (µmol/s) at which lung gas is exchanged with
    /// the atmosphere.
    public float VentilationRate;
    public long LungCapacityUMol;
    public long PlasmaCapacityUMol;
    public long PlasmaFiltrationRateUMol;
    public long PlasmaExchangeRate;
    public float BodyTemperature;
    public float CenterElectronegativity;
    public float ReactivityTolerance;
    public float AllostericSensitivity;
  }
}
