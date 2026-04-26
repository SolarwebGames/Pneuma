using System;
using System.Collections.Generic;
using SolarWeb.Pneuma.Atoms;
using SolarWeb.Pneuma.Data;
using SolarWeb.Pneuma.Grid;
using Unity.Collections;
using Unity.Mathematics;
using static SolarWeb.Pneuma.Atoms.Formulas;

namespace SolarWeb.Pneuma.Gas
{
  public class GasRegistry : IDisposable
  {
    public GasDefinition[] AllGases = null!;
    private string[] idFormulaLookup = null!;
    private readonly Dictionary<string, GasDefinition> allGases = new();
    private readonly Dictionary<string, string> gasAliases = new();
    private AtomicRegistry atomicRegistry = null!;
    public GasDataBuffer GasData;
    public int GasCount;

    public void Initialize(List<GasDefinition> gases, AtomicRegistry atomicRegistry, GasStoichiometry stoichiometry)
    {
      GasCount = gases.Count;
      int totalAtomEntries = 0;
      this.atomicRegistry = atomicRegistry;

      List<int[]> compositions = new();
      AllGases = gases.ToArray();
      idFormulaLookup = new string[GasCount];

      for (var i = 0; i < gases.Count; i++)
      {
        var gas = gases[i];
        gas.Id = i;
        var parsedAtoms = ParseFormula(gas.ChemicalFormula, atomicRegistry);
        var normalizedFormula = Normalize(gas.ChemicalFormula, atomicRegistry, parsedAtoms);
        gas.ChemicalFormula = normalizedFormula;
        int totalAtomsInGas = 0;
        foreach (var kvp in parsedAtoms) totalAtomsInGas += kvp.Value.Count;
        allGases[normalizedFormula] = gas;
        idFormulaLookup[gas.Id] = normalizedFormula;
        int[] indices = new int[totalAtomsInGas];
        int startIndex = 0;
        foreach (var kvp in parsedAtoms)
        {
          for (int j = 0; j < kvp.Value.Count; j++) indices[startIndex++] = kvp.Value.Definition.AtomicNumber - 1;
        }

        compositions.Add(indices);
        totalAtomEntries += indices.Length;
      }

      // Find max atomic number to size ElementOxideGasId
      int maxAtomicNumber = 0;
      foreach (var gas in AllGases)
      {
        var parsed = Formulas.ParseFormula(gas.ChemicalFormula, atomicRegistry);
        foreach (var atom in parsed.Values) maxAtomicNumber = Math.Max(maxAtomicNumber, atom.Definition.AtomicNumber);
      }

      GasData = new GasDataBuffer
      {
        MolarMass = new NativeArray<float>(GasCount, Allocator.Persistent),
        CollisionDiameter = new NativeArray<float>(GasCount, Allocator.Persistent),
        MeanElectronegativity = new NativeArray<float>(GasCount, Allocator.Persistent),
        ChemicalAffinity = new NativeArray<float>(GasCount, Allocator.Persistent),
        PlasmaSolubility = new NativeArray<float>(GasCount, Allocator.Persistent),
        StructuralWarpingPotential = new NativeArray<float>(GasCount, Allocator.Persistent),
        Radioactivity = new NativeArray<float>(GasCount, Allocator.Persistent),
        AtomicCompositionIndices = new NativeArray<int>(totalAtomEntries, Allocator.Persistent),
        CompositionOffsets = new NativeArray<int>(GasCount + 1, Allocator.Persistent),
        ElementOxideGasId = new NativeArray<int>(maxAtomicNumber, Allocator.Persistent),
        ElementOxideStoichiometry = new NativeArray<float>(maxAtomicNumber, Allocator.Persistent),
        S_Constants = new NativeArray<float>(GasCount, Allocator.Persistent),
        OxidizingPotency = new NativeArray<float>(GasCount, Allocator.Persistent),
        EnthalpyOfCombustion_Jmol = new NativeArray<float>(GasCount, Allocator.Persistent),
        CombustionColor = new NativeArray<float3>(GasCount, Allocator.Persistent),
        OverlayColor = new NativeArray<float3>(GasCount, Allocator.Persistent),
        MolarHeatCapacityCp = new NativeArray<float>(GasCount, Allocator.Persistent),
        MolarHeatCapacityCv = new NativeArray<float>(GasCount, Allocator.Persistent),
        ReferenceViscosity_uPas = new NativeArray<float>(GasCount, Allocator.Persistent),
        SutherlandConstant_K = new NativeArray<float>(GasCount, Allocator.Persistent),
        ThermalConductivity_WmK = new NativeArray<float>(GasCount, Allocator.Persistent),

        LowerExplosiveLimit = new NativeArray<float>(GasCount, Allocator.Persistent),
        UpperExplosiveLimit = new NativeArray<float>(GasCount, Allocator.Persistent),
        AutoIgnitionTemperature = new NativeArray<float>(GasCount, Allocator.Persistent),
        MeltingPoint_K = new NativeArray<float>(GasCount, Allocator.Persistent),

        AntoineA = new NativeArray<float>(GasCount, Allocator.Persistent),
        AntoineB = new NativeArray<float>(GasCount, Allocator.Persistent),
        AntoineC = new NativeArray<float>(GasCount, Allocator.Persistent),

        MolarMassScaled = new NativeArray<float>(GasCount, Allocator.Persistent),

        Corrosiveness = new NativeArray<float>(GasCount, Allocator.Persistent),
        BioInterference = new NativeArray<float>(GasCount, Allocator.Persistent),
        IonizingPotential = new NativeArray<float>(GasCount, Allocator.Persistent),
      };

      // Initialize oxide mapping to -1
      for (int i = 0; i < maxAtomicNumber; i++) GasData.ElementOxideGasId[i] = -1;

      int currentOffset = 0;
      for (int i = 0; i < gases.Count; i++)
      {
        var gas = gases[i];
        var indices = compositions[i];
        GasPhysicsAnalyzer.DeriveProperties(atomicRegistry, gas, stoichiometry);

        GasData.MolarMass[i] = gas.Properties.MolarMass;
        GasData.CollisionDiameter[i] = gas.Properties.CollisionDiameterAngstroms;
        GasData.MeanElectronegativity[i] = gas.Properties.MeanElectronegativity;
        GasData.ChemicalAffinity[i] = gas.Properties.ChemicalAffinity;
        GasData.PlasmaSolubility[i] = gas.Properties.PlasmaSolubility;
        GasData.StructuralWarpingPotential[i] = gas.Properties.StructuralWarpingPotential;
        GasData.Radioactivity[i] = gas.Properties.Radioactivity;
        GasData.S_Constants[i] = gas.Properties.DiffusionConstant;
        GasData.OxidizingPotency[i] = gas.Properties.OxidizingPotency;
        GasData.EnthalpyOfCombustion_Jmol[i] = gas.Properties.EnthalpyOfCombustion_Jmol;
        GasData.CombustionColor[i] = gas.CombustionColor;
        GasData.OverlayColor[i] = gas.OverlayColor;
        GasData.MolarHeatCapacityCp[i] = gas.Properties.MolarHeatCapacityCp;
        GasData.MolarHeatCapacityCv[i] = gas.Properties.MolarHeatCapacityCv;
        GasData.ReferenceViscosity_uPas[i] = gas.Properties.ReferenceViscosity_uPas;
        GasData.SutherlandConstant_K[i] = gas.Properties.SutherlandConstant_K;
        GasData.ThermalConductivity_WmK[i] = gas.Properties.GasThermalConductivity_WmK;

        GasData.LowerExplosiveLimit[i] = gas.Properties.LowerExplosiveLimit;
        GasData.UpperExplosiveLimit[i] = gas.Properties.UpperExplosiveLimit;
        GasData.AutoIgnitionTemperature[i] = gas.Properties.AutoIgnitionTemperature;
        GasData.MeltingPoint_K[i] = gas.Properties.MeltingPoint_K;

        GasData.AntoineA[i] = gas.Properties.AntoineA;
        GasData.AntoineB[i] = gas.Properties.AntoineB;
        GasData.AntoineC[i] = gas.Properties.AntoineC;

        GasData.MolarMassScaled[i] = gas.Properties.MolarMass * 1e-6f;

        GasData.Corrosiveness[i] = gas.Properties.Corrosiveness;
        GasData.BioInterference[i] = gas.Properties.BioInterference;
        GasData.IonizingPotential[i] = gas.Properties.IonizingPotential;

        if (GasData.LowerExplosiveLimit[i] > 0)
        {
          GasData.FuelGasMask |= (1UL << i);
        }

        GasData.CompositionOffsets[i] = currentOffset;
        for (int j = 0; j < indices.Length; j++)
        {
          int atomicIdx = indices[j];
          GasData.AtomicCompositionIndices[currentOffset + j] = atomicIdx;
        }
        currentOffset += indices.Length;
      }
      GasData.CompositionOffsets[GasCount] = currentOffset;

      // Build condensable gas index list (gases with AntoineA != 0).
      var tempCondensable = new List<int>();
      for (int i = 0; i < GasCount; i++)
        if (GasData.AntoineA[i] != 0f) tempCondensable.Add(i);
      GasData.CondensableGasCount = tempCondensable.Count;
      GasData.CondensableGasIndices = new NativeArray<int>(
        tempCondensable.Count == 0 ? 1 : tempCondensable.Count, Allocator.Persistent);
      for (int i = 0; i < tempCondensable.Count; i++)
        GasData.CondensableGasIndices[i] = tempCondensable[i];

      // Final pass to resolve oxides correctly using the gas dictionary
      foreach (var gas in AllGases)
      {
        var parsed = Formulas.ParseFormula(gas.ChemicalFormula, atomicRegistry);
        foreach (var pair in parsed.Values)
        {
          var atom = pair.Definition;
          int atomicIdx = atom.AtomicNumber - 1;
          if (atomicIdx < maxAtomicNumber && GasData.ElementOxideGasId[atomicIdx] == -1)
          {
            string standard = atom.Properties.StandardOxideFormula;
            string fallback = atom.Properties.FallbackOxideFormula;

            if (string.IsNullOrEmpty(standard)) continue; // Not an oxidizable element

            GasDefinition? resolvedGas = null;

            // 1. Try Standard
            if (allGases.TryGetValue(standard, out var oxideGas))
            {
              resolvedGas = oxideGas;
            }
            // 2. Try Fallback
            else if (!string.IsNullOrEmpty(fallback) && allGases.TryGetValue(fallback, out oxideGas))
            {
              resolvedGas = oxideGas;
            }
            // 3. Global Backup
            else if (allGases.TryGetValue("CO2", out oxideGas))
            {
              resolvedGas = oxideGas;
            }

            if (resolvedGas != null)
            {
              GasData.ElementOxideGasId[atomicIdx] = resolvedGas.Id;
              var oxideParsed = Formulas.ParseFormula(resolvedGas.ChemicalFormula, atomicRegistry);
              if (oxideParsed.TryGetValue(atom.Symbol, out var oxidePair))
              {
                GasData.ElementOxideStoichiometry[atomicIdx] = oxidePair.Count;
              }
              else
              {
                // Element is not in the byproduct (e.g., Silicon -> CO2)
                // Use 1:1 molar fallback
                GasData.ElementOxideStoichiometry[atomicIdx] = 1f;
              }
            }
          }
        }
      }
    }

    public GasDefinition GetGasFromFormula(string formula)
    {
      var normalizedFormula = formula;

      if (!allGases.TryGetValue(normalizedFormula, out var gas))
      {
        if (!gasAliases.TryGetValue(formula, out var normalized))
        {
          normalized = Normalize(formula, atomicRegistry);
          gasAliases[formula] = normalized;
        }

        if (!allGases.TryGetValue(normalized, out gas))
        {
          throw new Exception($"Gas with formula {formula} not found (normalized: {normalized})");
        }
      }

      return gas;
    }


    public void Dispose()
    {
      GasData.MolarMass.SafeDispose();
      GasData.CollisionDiameter.SafeDispose();
      GasData.MeanElectronegativity.SafeDispose();
      GasData.ChemicalAffinity.SafeDispose();
      GasData.PlasmaSolubility.SafeDispose();
      GasData.StructuralWarpingPotential.SafeDispose();
      GasData.Radioactivity.SafeDispose();
      GasData.AtomicCompositionIndices.SafeDispose();
      GasData.CompositionOffsets.SafeDispose();
      GasData.S_Constants.SafeDispose();
      GasData.OxidizingPotency.SafeDispose();
      GasData.EnthalpyOfCombustion_Jmol.SafeDispose();
      GasData.CombustionColor.SafeDispose();
      GasData.OverlayColor.SafeDispose();
      GasData.ElementOxideGasId.SafeDispose();
      GasData.ElementOxideStoichiometry.SafeDispose();
      GasData.MolarHeatCapacityCp.SafeDispose();
      GasData.MolarHeatCapacityCv.SafeDispose();
      GasData.ReferenceViscosity_uPas.SafeDispose();
      GasData.SutherlandConstant_K.SafeDispose();
      GasData.ThermalConductivity_WmK.SafeDispose();

      GasData.LowerExplosiveLimit.SafeDispose();
      GasData.UpperExplosiveLimit.SafeDispose();
      GasData.AutoIgnitionTemperature.SafeDispose();
      GasData.MeltingPoint_K.SafeDispose();

      GasData.AntoineA.SafeDispose();
      GasData.AntoineB.SafeDispose();
      GasData.AntoineC.SafeDispose();

      GasData.MolarMassScaled.SafeDispose();
      GasData.CondensableGasIndices.SafeDispose();
    }
  }
}
