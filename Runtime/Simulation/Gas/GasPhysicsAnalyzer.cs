using System;
using System.Collections.Generic;
using SolarWeb.Pneuma.Atoms;
using SolarWeb.Pneuma.Gas;
using SolarWeb.Pneuma.Grid;
using Unity.Mathematics;

public static class GasPhysicsAnalyzer
{
  /// <summary>Reference temperature for all transport property derivations [K].</summary>
  private const float T_REF = 273.15f;

  public static void DeriveProperties(AtomicRegistry registry, GasDefinition gas, GasStoichiometry stoichiometry)
  {
    // 1. Parse formula
    var components = Formulas.ParseFormula(gas.ChemicalFormula, registry);

    // Preserve manually-set fields
    GasProperties props = gas.Properties;

    // 2. Fundamental summations
    float totalMass = 0;
    float totalElectronegativity = 0;
    int totalAtoms = 0;
    float totalVolume_A3 = 0;
    float weightedTc = 0;
    float totalMeltingPoint = 0;

    // Clear stoichiometry for this gas
    int gasId = gas.Id;
    int totalAtomCount = stoichiometry?.TotalAtomCount ?? 0;
    int baseIdx = gasId * totalAtomCount;
    uint4 mask = 0;

    if (stoichiometry != null && stoichiometry.AtomicWeights.IsCreated)
    {
      for (int i = 0; i < totalAtomCount; i++) stoichiometry.AtomicWeights[baseIdx + i] = 0;
    }

    foreach (var pair in components.Values)
    {
      var atomDef = pair.Definition;
      var atomProps = atomDef.Properties;
      int n = pair.Count;

      totalMass += atomProps.AtomicMass_u * n;
      totalElectronegativity += atomProps.Electronegativity_Pauling * n;
      totalAtoms += n;

      float r_A = atomProps.AtomicRadius_pm / 100f;
      totalVolume_A3 += 4f / 3f * (float)Math.PI * r_A * r_A * r_A * n;

      weightedTc += atomProps.CriticalTemp_K * n;
      totalMeltingPoint += atomProps.MeltingPoint_K * n;

      // Generalized Stoichiometry
      int atomicNumber = atomDef.AtomicNumber;
      if (stoichiometry != null && stoichiometry.AtomicWeights.IsCreated && atomicNumber > 0 && atomicNumber < totalAtomCount)
      {
        stoichiometry.AtomicWeights[baseIdx + atomicNumber] = n;

        // Populate the uint4 mask (supporting up to 128 elements for fast-failing)
        int uintIdx = atomicNumber / 32;
        int bitIdx = atomicNumber % 32;
        if (uintIdx == 0) mask.x |= (uint)(1 << bitIdx);
        else if (uintIdx == 1) mask.y |= (uint)(1 << bitIdx);
        else if (uintIdx == 2) mask.z |= (uint)(1 << bitIdx);
        else if (uintIdx == 3) mask.w |= (uint)(1 << bitIdx);
      }
    }

    if (stoichiometry != null && stoichiometry.AtomicMasks.IsCreated)
    {
      stoichiometry.AtomicMasks[gasId] = mask;
    }

    props.AtomCount = totalAtoms;
    props.MolarMass = totalMass;
    props.MeanElectronegativity = totalElectronegativity / totalAtoms;

    if (props.MeltingPoint_K <= 0f)
      props.MeltingPoint_K = totalMeltingPoint / totalAtoms;

    // Enthalpy Of Combustion
    if (props.EnthalpyOfCombustion_Jmol <= 0f)
    {
      float totalEnthalpy = 0;
      foreach (var pair in components.Values)
      {
        totalEnthalpy += pair.Definition.Properties.OxidationEnthalpy_Jmol * pair.Count;
      }
      props.EnthalpyOfCombustion_Jmol = totalEnthalpy;
    }

    // Save derived props back to the gas definition
    gas.Properties = props;

    // Oxidizing Potency + Halogen Acid Corrosiveness
    // Halogen acid corrosiveness is always derived from molecular composition: halogens dissolve
    // in tissue moisture to form hydrohalic acids (HF > HCl > HBr > HI), independent of whether
    // OxidizingPotency was manually set in XML.
    float halogenCorrosiveness = 0f;
    foreach (var pair in components.Values)
    {
      var atom = pair.Definition;
      int n = pair.Count;
      halogenCorrosiveness += atom.Symbol switch
      {
        "F" => 1.5f * n,
        "Cl" => 0.8f * n,
        "Br" => 0.4f * n,
        "I" => 0.2f * n,
        _ => 0f
      };
    }

    if (props.OxidizingPotency <= 0f)
    {
      float potency = 0;
      foreach (var pair in components.Values)
      {
        var atom = pair.Definition;
        int n = pair.Count;
        potency += atom.Symbol switch
        {
          "O" => 0.5f * n,
          "F" => 1.0f * n,
          "Cl" => 0.75f * n,
          "Br" => 0.4f * n,
          "I" => 0.2f * n,
          _ => 0f
        };
      }
      props.OxidizingPotency = potency;
    }

    // 3. Molecular geometry and degrees of freedom
    var isLinear = (totalAtoms == 2) || props.IsLinear is true;
    props.IsLinear = isLinear;
    props.DegreesOfFreedom = totalAtoms == 1 ? 3 : (isLinear ? 5 : 6);

    // 4. Heat capacities via equipartition
    const float R = 8.314f;
    props.MolarHeatCapacityCv = (props.DegreesOfFreedom / 2.0f) * R;
    props.MolarHeatCapacityCp = props.MolarHeatCapacityCv + R;
    props.Gamma = props.MolarHeatCapacityCp / props.MolarHeatCapacityCv;

    // 5. Collision diameter
    if (props.CollisionDiameterAngstroms <= 0f)
    {
      float effectiveRadius_A = (float)Math.Pow(3.0 * totalVolume_A3 / (4.0 * Math.PI), 1.0 / 3.0);
      props.CollisionDiameterAngstroms = 2f * effectiveRadius_A;
    }

    // 6. Lennard-Jones well depth
    if (props.LJWellDepth_K <= 0f)
      props.LJWellDepth_K = 0.75f * (weightedTc / totalAtoms);

    // 7. Diffusion volume
    props.DiffusionVolume = CalculateDiffusionVolume(components, props.RingCount);

    // 8. Binary Diffusion Constant
    {
      float v_air = 20.1f;
      float m_air = 28.97f;
      float m_gas = props.MolarMass;
      float m_avg = 2.0f / (1.0f / m_gas + 1.0f / m_air);
      float sum_v = (float)Math.Pow(Math.Pow(props.DiffusionVolume, 1.0 / 3.0) + Math.Pow(v_air, 1.0 / 3.0), 2.0);
      float coreD = (0.00143f * (float)Math.Pow(273.15, 1.75)) / (1.0f * (float)Math.Sqrt(m_avg) * sum_v);
      const float GameplayScalar = 500.0f;
      props.DiffusionConstant = coreD * GameplayScalar;
    }

    // 9. Chapman-Enskog viscosity
    {
      float Tstar = T_REF / Math.Max(props.LJWellDepth_K, 1f);
      float omega22 = CollisionIntegral22(Tstar);
      float sigma = props.CollisionDiameterAngstroms;
      props.ReferenceViscosity_uPas = 2.6693f * (float)Math.Sqrt(props.MolarMass * T_REF)
                                      / (sigma * sigma * omega22);
    }

    // 10. Thermal conductivity
    {
      float eta_Pas = props.ReferenceViscosity_uPas * 1e-6f;
      float M_kgmol = props.MolarMass * 1e-3f;
      props.GasThermalConductivity_WmK = 0.25f * (9f * props.Gamma - 5f)
                                         * eta_Pas * props.MolarHeatCapacityCv / M_kgmol;
    }

    // 11. Sutherland's constant
    if (props.SutherlandConstant_K <= 0f)
      props.SutherlandConstant_K = 1.47f * props.LJWellDepth_K;

    // 12. Structural Warping Potential
    if (props.StructuralWarpingPotential <= 0f)
    {
      // Homonuclear non-polar gases (like N2, O2, H2, and Noble gases) are generally poor 
      // allosteric effectors because they lack the dipole or reactivity to significantly 
      // warp protein structures. 
      bool isSimpleInert = (components.Count == 1) && (props.DipoleMoment_Debye < 0.01f);

      if (isSimpleInert)
      {
        props.StructuralWarpingPotential = 0f;
      }
      else
      {
        // Potential is driven by structural warping (dipole) and atomic "grip" (EN).
        // Thresholding the EN ensures that background gases with moderate EN don't
        // create a "noise" floor.
        float polarityFactor = math.max(0f, props.MeanElectronegativity - 2.5f);
        props.StructuralWarpingPotential = (polarityFactor * 1.5f) + (props.DipoleMoment_Debye * 2.0f);
      }
    }

    // 13. Generalized Hazard Profile
    props.Corrosiveness = Math.Max(0f, props.OxidizingPotency - 1.0f) * 10f + halogenCorrosiveness;

    // BioInterference: polar disruption of biochemical pathways via electrochemical deviation.
    // Uses symmetric absolute deviation from the neutrality midpoint (EN ≈ 2.6):
    //   High EN (HF, SO2): electrophilic/acidic — attacks nucleophilic sites in macromolecules.
    //   Low EN (NH3, PH3): nucleophilic/basic — attacks electrophilic sites (carbonyls, metal cofactors).
    // H2O (EN ≈ 2.613) scores ≈ 0.024, safely below the default hazardThreshold of 0.25.
    // NH3 (EN ≈ 2.41) scores ≈ 0.28, correctly above threshold — alkaline hydrolysis is real damage.
    props.BioInterference = props.DipoleMoment_Debye * Math.Abs(props.MeanElectronegativity - 2.6f);

    props.IonizingPotential = props.Radioactivity * 100f;

    gas.Properties = props;
  }

  public static float CalculateDiffusionVolume(Dictionary<string, Formulas.AtomCountPair> components, int ringCount)
  {
    float volume = 0;
    foreach (var pair in components.Values)
    {
      volume += GetAtomicDiffusionContribution(pair.Definition) * pair.Count;
    }
    if (ringCount > 0)
      volume -= 18.3f * ringCount;
    return Math.Max(volume, 1.0f);
  }

  private static float GetAtomicDiffusionContribution(AtomDefinition def)
  {
    return def.AtomicNumber switch
    {
      1 => 2.31f,  // Hydrogen
      6 => 15.9f,  // Carbon
      7 => 5.67f,  // Nitrogen
      8 => 5.42f,  // Oxygen
      9 => 5.97f,  // Fluorine
      16 => 17.0f,  // Sulfur
      17 => 21.0f,  // Chlorine
      35 => 21.9f,  // Bromine
      53 => 29.8f,  // Iodine
      _ => def.Properties.AtomicRadius_pm / 10f
    };
  }

  private static float CollisionIntegral22(float Tstar)
  {
    Tstar = Math.Max(Tstar, 0.3f);
    return 1.16145f / (float)Math.Pow(Tstar, 0.14874)
         + 0.52487f / (float)Math.Exp(0.7732 * Tstar)
         + 2.16178f / (float)Math.Exp(2.43787 * Tstar);
  }

  public static float GetSConstant(GasProperties properties, GasProperties baseline)
  {
    float massRatio = properties.MolarMass / baseline.MolarMass;
    float polarityFactor = properties.MeanElectronegativity
                           / Math.Max(baseline.MeanElectronegativity, 0.1f);
    const float sBaseline = 111.0f;
    return sBaseline * massRatio * polarityFactor;
  }
}
