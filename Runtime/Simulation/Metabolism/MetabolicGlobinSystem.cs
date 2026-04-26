using System;
using System.Collections.Generic;
using Unity.Collections;

using SolarWeb.Pneuma.Atoms;
using SolarWeb.Pneuma.Gas;

namespace SolarWeb.Pneuma.Metabolism
{
  public class MetabolicGlobinSystem : IDisposable
  {
    public NativeArray<long> GlobinCapacities;
    public NativeArray<float> GlobinHillCoefficients;
    public NativeArray<bool> GlobinIsCompetitive;

    public NativeArray<int> GasToGlobinIndex;
    public NativeArray<float> GasBindingAffinities;
    public NativeArray<float> GasMolarMasses;

    public int GlobinCount { get; private set; }
    public int GasCount { get; private set; }

    public MetabolicGlobinSystem(List<StorageGlobinCriteria> criteriaList, GasRegistry gasRegistry, AtomicRegistry atomicRegistry)
    {
      GlobinCount = criteriaList.Count;
      GasCount = gasRegistry.GasCount;

      GlobinCapacities = new NativeArray<long>(GlobinCount, Allocator.Persistent);
      GlobinHillCoefficients = new NativeArray<float>(GlobinCount, Allocator.Persistent);
      GlobinIsCompetitive = new NativeArray<bool>(GlobinCount, Allocator.Persistent);

      GasToGlobinIndex = new NativeArray<int>(GasCount, Allocator.Persistent);
      GasBindingAffinities = new NativeArray<float>(GasCount, Allocator.Persistent);
      GasMolarMasses = gasRegistry.GasData.MolarMass;

      for (int i = 0; i < GasCount; i++)
      {
        GasToGlobinIndex[i] = -1;
        GasBindingAffinities[i] = 0f;
      }

      // 3. Process Criteria into Arrays
      for (int b = 0; b < GlobinCount; b++)
      {
        var criteria = criteriaList[b];

        // --- Fill Globin Definitions ---
        GlobinCapacities[b] = criteria.CapacityUMol;
        GlobinHillCoefficients[b] = criteria.HillCoefficient;
        GlobinIsCompetitive[b] = criteria.IsCompetitive;

        // --- Map Gases to this Globin ---
        // We iterate all gases to see if they match this globin's filter
        foreach (var gas in gasRegistry.AllGases)
        {
          // First-match-wins: only assign if not already mapped to a higher-priority globin
          if (GasToGlobinIndex[gas.Id] == -1 && MatchesCriteria(gas, criteria, atomicRegistry))
          {
            // Assign gas to this globin
            GasToGlobinIndex[gas.Id] = b;

            // Derive affinity based on globin's base affinity + gas properties
            float specificAffinity = criteria.BaseAffinity * gas.Properties.ChemicalAffinity;
            GasBindingAffinities[gas.Id] = specificAffinity;
          }
        }
      }
    }

    private bool MatchesCriteria(GasDefinition gas, StorageGlobinCriteria criteria, AtomicRegistry registry)
    {
      var props = gas.Properties;

      // 1. Steric check (pm)
      float gasDiameterPm = props.CollisionDiameterAngstroms * 100f;
      if (gasDiameterPm > criteria.DerivedMaxCollisionDiameter) return false;

      // 2. Electronegativity check
      if (props.MeanElectronegativity < criteria.DerivedMinElectronegativity) return false;

      // 3. Coordination check
      var components = Formulas.ParseFormula(gas.ChemicalFormula, registry);
      foreach (var symbol in components.Keys)
      {
        if (AtomPhysicsAnalyzer.CanFormCoordinateBond(registry, symbol, criteria.MetalSymbol.ToString()))
          return true;
      }

      return false;
    }

    public void Dispose()
    {
      if (GlobinCapacities.IsCreated) GlobinCapacities.Dispose();
      if (GlobinHillCoefficients.IsCreated) GlobinHillCoefficients.Dispose();
      if (GlobinIsCompetitive.IsCreated) GlobinIsCompetitive.Dispose();
      if (GasToGlobinIndex.IsCreated) GasToGlobinIndex.Dispose();
      if (GasBindingAffinities.IsCreated) GasBindingAffinities.Dispose();
    }
  }
}