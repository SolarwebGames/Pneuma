namespace SolarWeb.Pneuma.Gas
{
  public struct GasProperties
  {
    public int DegreesOfFreedom;
    public bool? IsLinear;
    public int RingCount;
    public float DiffusionVolume;

    /// <summary>
    /// Lennard-Jones collision diameter σ [Å].
    /// Derived from atomic volumes; set manually per gas for accuracy.
    /// </summary>
    public float CollisionDiameterAngstroms;

    /// <summary>
    /// Lennard-Jones well depth ε/k [K].
    /// Estimated from weighted atom critical temperatures; set manually per gas for accuracy.
    /// Notably unreliable for heterogeneous polar molecules (H2O, CO2, NH3, etc.).
    /// </summary>
    public float LJWellDepth_K;

    public float Radioactivity;

    /// <summary>
    /// Gas binding/affinity modifier for metabolic gas transport (dimensionless, gameplay value).
    /// Scales how readily this gas binds to blood carriers and dissolves in plasma.
    /// Example values: N2=1 (inert), O2=50 (active), CO=1000 (hemoglobin hijacker).
    /// Do NOT use for viscosity — see SutherlandConstant_K.
    /// </summary>
    public float ChemicalAffinity;

    /// <summary>
    /// Sutherland's constant S [K] for temperature-dependent viscosity:
    /// η(T) = η_ref × (T_ref + S)/(T + S) × (T/T_ref)^1.5
    /// Estimated as 1.47 × LJWellDepth_K; set manually per gas for accuracy.
    /// </summary>
    public float SutherlandConstant_K;

    public float PlasmaSolubility;

    /// <summary>
    /// The physical potential of a gas molecule to warp generic protein structures
    /// via dipole-dipole interactions or electronegative attraction. Decoupled from 
    /// biological sensitivity.
    /// </summary>
    public float StructuralWarpingPotential;

    public float LowerExplosiveLimit;
    public float UpperExplosiveLimit;
    public float AutoIgnitionTemperature;

    // --- Derived transport properties (at 273.15 K reference) ---

    /// <summary>Dynamic viscosity at 273.15 K [μPa·s]. Derived via Chapman-Enskog theory.</summary>
    public float ReferenceViscosity_uPas;

    /// <summary>
    /// Gas-phase thermal conductivity at 273.15 K [W/m·K].
    /// Derived via the Eucken approximation (λ = (1/4)(9γ-5) η Cv/M).
    /// Accurate to ~10-20% for non-polar gases; underestimates for polar gases.
    /// </summary>
    public float GasThermalConductivity_WmK;

    // --- Manually specified per gas — cannot be derived from constituent atom data ---

    /// <summary>
    /// Van der Waals intermolecular attraction [Pa·m⁶·mol⁻²].
    /// 0 = treat as ideal gas.
    /// Feeds into (P + a·n²/V²)(V − n·b) = nRT for real-gas PVT.
    /// Example values: N2=0.137, CO2=0.366, H2O=0.554, NH3=0.423.
    /// </summary>
    public float VanDerWaalsA;

    /// <summary>
    /// Van der Waals excluded volume [m³·mol⁻¹].
    /// 0 = treat as ideal gas.
    /// Example values: N2=3.87e-5, CO2=4.29e-5, H2O=3.05e-5, NH3=3.71e-5.
    /// </summary>
    public float VanDerWaalsB;

    /// <summary>
    /// Pitzer acentric factor ω [-].
    /// Used in Peng-Robinson and Soave-Redlich-Kwong equations of state.
    /// 0 ≈ spherical non-polar (noble gases). Example values: N2=0.040, CO2=0.225, H2O=0.345.
    /// </summary>
    public float AcentricFactor;

    /// <summary>
    /// Electric dipole moment [Debye].
    /// 0 = non-polar. Required for accurate viscosity mixing rules and polar intermolecular forces.
    /// Example values: H2O=1.85, NH3=1.47, SO2=1.63, CO=0.11, N2=0, CO2=0.
    /// </summary>
    public float DipoleMoment_Debye;

    // --- Phase transition (condensation / evaporation) ---

    /// <summary>
    /// Antoine equation coefficient A: log₁₀(P_bar) = A − B / (C + T_K).
    /// Zero means this gas has no liquid phase in the simulation temperature range.
    /// </summary>
    public float AntoineA;

    /// <summary>Antoine equation coefficient B [K].</summary>
    public float AntoineB;

    /// <summary>Antoine equation coefficient C [K].</summary>
    public float AntoineC;

    /// <summary>Normal boiling point [K] at 1 atm. Informational; Antoine equation is used at runtime.</summary>
    public float BoilingPoint_K;
    public float MeltingPoint_K;

    // --- Derived properties ---
    public int AtomCount;
    public float MolarMass;
    public float MolarHeatCapacityCp; // Heat Capacity (Constant Pressure) [J/mol·K]
    public float MolarHeatCapacityCv; // Heat Capacity (Constant Volume)   [J/mol·K]
    public float Gamma;               // Cp/Cv (adiabatic index)
    public float MeanElectronegativity;
    public float OxidizingPotency;    // Strength as an oxidizer (O2 = 1.0 baseline)
    public float EnthalpyOfCombustion_Jmol; // Energy released per mole of fuel burned [J/mol]

    public float DiffusionConstant;   // Pre-factor for Fuller-Schettler-Giddings binary diffusion; apply T^1.75 / P at runtime

    // --- Bio-Stoichiometry and Hazards (Generalized) ---

    public float Corrosiveness;       // Potential for oxidative/chemical damage to tissue (derived from OxidizingPotency)
    public float BioInterference;     // Potential for polar disruption of bio-pathways (derived from DipoleMoment/Electronegativity)
    public float IonizingPotential;   // Damage from airborne radioisotopes (derived from Radioactivity)
  }
}
