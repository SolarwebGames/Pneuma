using System;
using System.Collections.Generic;
using Unity.Collections;

using SolarWeb.Pneuma.Atoms;
using SolarWeb.Pneuma.Gas;

namespace SolarWeb.Pneuma.Metabolism
{
  public struct MetabolismBuffer : IDisposable
  {
    public MetabolismProperties Properties;

    public NativeArray<MetabolicReactionProperties> Reactions;
    public NativeArray<MetabolicDemandProperties> Demands;
    public NativeArray<long> ExcretionEfficienciesBP; // Indexed by gas ID, size = GasCount

    public void Initialize(GasMetabolism source, GasRegistry gasRegistry, AtomicRegistry atomicRegistry)
    {
      Properties = source.Properties;

      var centerMetal = atomicRegistry.Get(source.CentralElementSymbol);
      Properties.CenterElectronegativity = centerMetal.Properties.Electronegativity_Pauling;

      // --- Reactions ---
      int reactionCount = source.Reactions.Count;
      Reactions = new NativeArray<MetabolicReactionProperties>(reactionCount, Allocator.Persistent);

      for (int i = 0; i < reactionCount; i++)
      {
        var reaction = source.Reactions[i];
        var props = reaction.Properties;
        props.InputId = gasRegistry.GetGasFromFormula(Formulas.Normalize(reaction.InputFormula, atomicRegistry)).Id;
        props.OutputId = gasRegistry.GetGasFromFormula(Formulas.Normalize(reaction.OutputFormula, atomicRegistry)).Id;
        props.TargetGlobinId = FindGlobinIndex(reaction.RequiredGlobinName, source.GlobinLevels);
        Reactions[i] = props;
      }

      // --- Demands ---
      int demandCount = source.Demands.Count;
      Demands = new NativeArray<MetabolicDemandProperties>(demandCount, Allocator.Persistent);

      for (int i = 0; i < demandCount; i++)
      {
        var demand = source.Demands[i];
        var props = demand.Properties;
        props.GasId = gasRegistry.GetGasFromFormula(Formulas.Normalize(demand.Formula, atomicRegistry)).Id;
        Demands[i] = props;
      }

      // --- Excretion (flat array indexed by gas ID) ---
      ExcretionEfficienciesBP = new NativeArray<long>(gasRegistry.GasCount, Allocator.Persistent);

      foreach (var excretion in source.ExcretableGases)
      {
        var gas = gasRegistry.GetGasFromFormula(Formulas.Normalize(excretion.Formula, atomicRegistry));
        ExcretionEfficienciesBP[gas.Id] = excretion.EfficiencyBP;
      }
    }

    private static int FindGlobinIndex(string globinName, List<StorageGlobinCriteria> globins)
    {
      for (int i = 0; i < globins.Count; i++)
      {
        if (globins[i].Name.ToString() == globinName) return i;
      }
      return -1;
    }

    public void Dispose()
    {
      if (Reactions.IsCreated) Reactions.Dispose();
      if (Demands.IsCreated) Demands.Dispose();
      if (ExcretionEfficienciesBP.IsCreated) ExcretionEfficienciesBP.Dispose();
    }
  }
}
