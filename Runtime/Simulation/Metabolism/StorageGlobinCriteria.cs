using System;
using SolarWeb.Pneuma.Atoms;
using Unity.Mathematics;

namespace SolarWeb.Pneuma.Metabolism
{
  [Serializable]
  public struct StorageGlobinCriteria
  {
    public string Name;
    public string MetalSymbol;

    public long CapacityUMol;
    public float HillCoefficient;
    /// <summary>
    /// Lung O2 fraction at which this carrier-protein reaches 50% saturation.
    /// Converts physiology's P50 (partial pressure) to a lung-fraction parameter:
    ///   P50_fraction = P50_kPa / P_atm_kPa  (e.g. human Hb: 3.6 / 101.325 ≈ 0.035)
    /// Species with higher-affinity blood (low P50) use a smaller value here;
    /// lower-affinity carriers (high P50) use a larger value.
    /// </summary>
    public float P50LungFraction;
    public float BaseAffinity;
    public bool IsCompetitive;

    // --- DERIVED FILTERS (Cached for performance) ---
    public float DerivedMaxCollisionDiameter;
    public float DerivedMinElectronegativity;
    public float DerivedMetalHardness; // 0.0 (Soft) to 1.0 (Hard)

    public void DeriveStorageCriteria(AtomicRegistry registry)
    {
      var metal = registry.Get(MetalSymbol.ToString());

      var props = metal.Properties;

      // 1. POCKET SIZE (Steric Hindrance)
      // AtomicRadius_pm is already in picometers.
      // Heuristic: Coordination sphere is roughly ~2.8x the atomic radius
      // Fe (126pm) -> ~353pm pocket (Fits O2 and CO)
      DerivedMaxCollisionDiameter = props.AtomicRadius_pm * 2.8f;

      // 2. REACTIVITY THRESHOLD
      // Use Pauling Electronegativity.
      // Metals with high EN (e.g. Gold 2.54) hold electrons tightly.
      // Ligands must be *at least* this electronegative to donate effectively.
      DerivedMinElectronegativity = props.Electronegativity_Pauling + 0.5f;

      // 3. CHEMICAL HARDNESS (HSAB Theory)
      // Use Ionization Energy as the primary driver for Metal Hardness.
      // Soft Acids (Au, Hg): Low Ionization (< 800 kJ/mol) -> 0.0
      // Hard Acids (Mg, Ca): High Ionization (> 1000 kJ/mol) -> 1.0
      // Borderline (Fe, Zn): Intermediate (~700-900) -> ~0.5

      // Map Range: 600 (Soft) to 1100 (Hard)
      DerivedMetalHardness = math.clamp((props.FirstIonizationEnergy_kJmol - 600f) / 500f, 0, 1);
    }
  }
}