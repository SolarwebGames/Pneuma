using System;

namespace SolarWeb.Pneuma.Atoms
{
  [Serializable]
  public class AtomDefinition
  {
    public string Symbol = string.Empty;
    public string Name = string.Empty;
    public int AtomicNumber;

    public AtomicProperties Properties;
  }
}