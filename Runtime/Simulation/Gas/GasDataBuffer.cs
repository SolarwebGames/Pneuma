using Unity.Collections;
using Unity.Mathematics;

namespace SolarWeb.Pneuma.Gas
{
  public struct GasDataBuffer
  {
    // Physics & Diffusion
    [ReadOnly] public NativeArray<float> MolarMass;
    [ReadOnly] public NativeArray<float> CollisionDiameter;
    [ReadOnly] public NativeArray<float> MeanElectronegativity;
    [ReadOnly] public NativeArray<float> OxidizingPotency;
    [ReadOnly] public NativeArray<float> EnthalpyOfCombustion_Jmol;
    [ReadOnly] public NativeArray<float3> CombustionColor;
    [ReadOnly] public NativeArray<float3> OverlayColor;

    // Metabolic & Solubility
    [ReadOnly] public NativeArray<float> ChemicalAffinity;
    [ReadOnly] public NativeArray<float> PlasmaSolubility;
    [ReadOnly] public NativeArray<float> StructuralWarpingPotential;
    [ReadOnly] public NativeArray<float> Radioactivity;

    [ReadOnly] public NativeArray<float> Corrosiveness;
    [ReadOnly] public NativeArray<float> BioInterference;
    [ReadOnly] public NativeArray<float> IonizingPotential;

    public ulong FuelGasMask;

    [ReadOnly] public NativeArray<int> AtomicCompositionIndices;
    [ReadOnly] public NativeArray<int> CompositionOffsets; // Where each gas starts in the indices array
    [ReadOnly] public NativeArray<int> ElementOxideGasId;   // [ElementIndex] -> Product GasId
    [ReadOnly] public NativeArray<float> ElementOxideStoichiometry; // [ElementIndex] -> Number of atoms of this element in one molecule of the oxide
    [ReadOnly] public NativeArray<float> MolarHeatCapacityCp;
    [ReadOnly] public NativeArray<float> MolarHeatCapacityCv;
    [ReadOnly] public NativeArray<float> S_Constants;

    // Transport properties (at 273.15 K reference, temperature-corrected at runtime)
    /// <summary>Dynamic viscosity at 273.15 K [μPa·s]. Derived via Chapman-Enskog theory.</summary>
    [ReadOnly] public NativeArray<float> ReferenceViscosity_uPas;

    [ReadOnly] public NativeArray<float> SutherlandConstant_K;

    [ReadOnly] public NativeArray<float> ThermalConductivity_WmK;

    // Ignition properties
    [ReadOnly] public NativeArray<float> LowerExplosiveLimit;
    [ReadOnly] public NativeArray<float> UpperExplosiveLimit;
    [ReadOnly] public NativeArray<float> AutoIgnitionTemperature;

    [ReadOnly] public NativeArray<float> MeltingPoint_K;

    [ReadOnly] public NativeArray<float> AntoineA;
    [ReadOnly] public NativeArray<float> AntoineB;
    [ReadOnly] public NativeArray<float> AntoineC;

    // Derived / precomputed helpers
    /// <summary>MolarMass * 1e-6f — eliminates per-element scaling in the sync inner loop.</summary>
    [ReadOnly] public NativeArray<float> MolarMassScaled;
    /// <summary>Indices of gases where AntoineA != 0 (condensable). Phase snapshot copies only these rows.</summary>
    [ReadOnly] public NativeArray<int> CondensableGasIndices;
    public int CondensableGasCount;

    public bool IsCreated => MolarMass.IsCreated;
  }
}