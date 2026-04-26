using System.Collections.Generic;

namespace SolarWeb.Pneuma.Metabolism
{
  public class GasMetabolism
  {
    public string Name = string.Empty;
    public int Id;
    public string CentralElementSymbol = string.Empty;
    public List<MetabolicReaction> Reactions = new();
    public List<MetabolicDemand> Demands = new();
    public List<GasFiltrationEfficiency> ExcretableGases = new();
    public List<StorageGlobinCriteria> GlobinLevels = new();
    public MetabolismProperties Properties = new();

    public bool IsSimplified = false;

    // XML-loadable generalized danger criteria
    public float MinTemperature = 273.15f;
    public float MaxTemperature = 323.15f;
    public float MinTemperatureFullDanger = 253.15f;
    public float MaxTemperatureFullDanger = 343.15f;

    public float MinPressureKpa = 50f;
    public float MaxPressureKpa = 200f;
    public float MinPressureFullDangerKpa = 20f;
    public float MaxPressureFullDangerKpa = 500f;

    public float RadiationResistance = 0f;
    public float EnthalpySensitivity = 1.0f;

    public float ReferenceBodyTempK = 310.15f;
    public float CausticResistance = 0f;
    public float RadiationSensitivity = 2.0f;
    public float CausticSensitivity = 1.0f;

    public List<GasDangerThresholdXML> GasDangerThresholds = new();
    public GasReaction GasReaction = new();
    public AllostericProfile AllostericProfile = new();

    // Per-biology gas hazard control (simplified metabolism)
    /// <summary>Gas formulas whose stress contribution is zeroed out entirely for this biology.</summary>
    public List<string> SafeGasFormulas = new();
    /// <summary>Explicit per-gas toxicity overrides. Replaces the derived SWP-based weight for the listed gas.</summary>
    public List<GasHazardOverride> HazardOverrides = new();
  }

  public class GasHazardOverride
  {
    public string Formula = string.Empty;
    public float Toxicity = 0f;
  }

  public class AllostericProfile
  {
    public float DipoleSensitivity = 1.5f;
    public float ElectronegativityThreshold = 2.5f;
    public List<string> ExemptGases = new();
  }

  public class GasDangerThresholdXML
  {
    public string Formula = string.Empty;
    public float MinFraction = -1f;
    public float MinFullDangerFraction = -1f;
    public float MaxFraction = 2f;
    public float MaxFullDangerFraction = 2f;
  }
}