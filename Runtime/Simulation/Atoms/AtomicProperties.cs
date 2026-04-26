using System.Collections.Generic;

namespace SolarWeb.Pneuma.Atoms
{
  public struct AtomicProperties
  {
    public int Period;
    public int Group;
    public PeriodicTableBlock Block;
    public ChemicalSeries Series;

    public List<int> OxidationStates;
    public int CommonValence;
    public string IonColorHex;

    public float AtomicMass_u;
    public float AtomicRadius_pm;

    public float Electronegativity_Pauling;
    public float FirstIonizationEnergy_kJmol;
    public float ElectronAffinity_kJmol;
    public int ValenceElectrons;

    /// <summary>Reference melting point at 1 bar. Use PhaseData.MeltingCurve for pressure-dependent values.</summary>
    public float MeltingPoint_K;
    /// <summary>Reference boiling point at 1 bar. Use PhaseData.VaporizationCurve for pressure-dependent values.</summary>
    public float BoilingPoint_K;
    public float EnthalpyFusion_kJmol;
    public float EnthalpyVaporization_kJmol;

    public float SpecificHeat_JgK;
    public float ThermalConductivity_WmK;

    public float LiquidDensity_gcm3;
    public float MohsHardness;

    public float CriticalTemp_K;
    public float CriticalPressure_Bar;

    public string EmissionHex;
    public bool IsStable;
    public float HalfLife_Sec;
    public DecayMode DecayMode;

    /// <summary>
    /// Full pressure-dependent phase boundary data.
    /// When null, the simulation falls back to Clausius-Clapeyron using
    /// MeltingPoint_K, BoilingPoint_K, and EnthalpyVaporization_kJmol.
    /// </summary>
    public PhaseData PhaseData;

    // Derived properties
    public float InverseRadius;
    public CoordinationProfile CoordinationProfile;

    // Oxidation & Combustion
    public string StandardOxideFormula;
    public string FallbackOxideFormula;
    public float OxidationEnthalpy_Jmol;
  }
}
