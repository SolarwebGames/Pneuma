using System;
using Unity.Mathematics;

namespace SolarWeb.Pneuma.Gas
{
  [Serializable]
  public class GasDefinition
  {
    public int Id;
    public string Name = string.Empty;
    public string ChemicalFormula = string.Empty;
    public float3 CombustionColor;
    public float3 OverlayColor;

    public GasProperties Properties;
  }
}