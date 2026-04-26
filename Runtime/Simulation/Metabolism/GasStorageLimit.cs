namespace SolarWeb.Pneuma.Metabolism
{
  public struct GasStorageLimit
  {
    public int GasId;
    public string Formula;
    public long MaxStorageUMol;
    public float CriticalThreshold; // % at which "Deficit" damage starts
  }
}