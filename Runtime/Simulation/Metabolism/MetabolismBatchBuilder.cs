using Unity.Collections;
using Unity.Mathematics;
using SolarWeb.Pneuma.Gas;
using SolarWeb.Pneuma.Atoms;

namespace SolarWeb.Pneuma.Metabolism
{
  public static class MetabolismBatchBuilder
  {
    public static void InitializeBatch(MetabolismBatch batch, GasMetabolism template, GasRegistry gasRegistry, AtomicRegistry atomicRegistry)
    {
      batch.GlobinCount = template.GlobinLevels.Count;
      batch.GlobinSpecs = new NativeArray<StorageGlobinCriteria>(batch.GlobinCount, Allocator.Persistent);
      batch.GlobinMaxCollisionDiameters = new NativeArray<float>(batch.GlobinCount, Allocator.Persistent);
      batch.GlobinMinElectronegativities = new NativeArray<float>(batch.GlobinCount, Allocator.Persistent);
      batch.GlobinMetalHardnesses = new NativeArray<float>(batch.GlobinCount, Allocator.Persistent);
      batch.GlobinCapacities = new NativeArray<long>(batch.GlobinCount, Allocator.Persistent);
      batch.InvGlobinCapacities = new NativeArray<float>(batch.GlobinCount, Allocator.Persistent);

      for (int i = 0; i < batch.GlobinCount; i++)
      {
        var criteria = template.GlobinLevels[i];
        criteria.DeriveStorageCriteria(atomicRegistry);
        template.GlobinLevels[i] = criteria;

        batch.GlobinSpecs[i] = criteria;
        batch.GlobinMaxCollisionDiameters[i] = criteria.DerivedMaxCollisionDiameter;
        batch.GlobinMinElectronegativities[i] = criteria.DerivedMinElectronegativity;
        batch.GlobinMetalHardnesses[i] = criteria.DerivedMetalHardness;
        batch.GlobinCapacities[i] = criteria.CapacityUMol;
        batch.InvGlobinCapacities[i] = 1.0f / math.max(1f, (float)criteria.CapacityUMol);
      }

      // --- Resolve reaction IDs, globin assignments, then sort globin-first ---
      var rawReactions = new MetabolicReactionProperties[template.Reactions.Count];
      for (int i = 0; i < template.Reactions.Count; i++)
      {
        var reaction = template.Reactions[i];
        var resolvedInput = gasRegistry.GetGasFromFormula(reaction.InputFormula);
        var resolvedOutput = gasRegistry.GetGasFromFormula(reaction.OutputFormula);
        reaction.Properties.InputId = resolvedInput.Id;
        reaction.Properties.OutputId = resolvedOutput.Id;
        reaction.Properties.TargetGlobinId = FindGlobinIndex(reaction.RequiredGlobinName, template);
        template.Reactions[i] = reaction;
        rawReactions[i] = reaction.Properties;
      }

      // Sort: globin reactions first, free reactions after (eliminates branch in hot loop)
      var sortedReactions = new MetabolicReactionProperties[rawReactions.Length];
      batch.GlobinReactionCount = 0;
      for (int r = 0; r < rawReactions.Length; r++)
        if (rawReactions[r].TargetGlobinId >= 0) batch.GlobinReactionCount++;

      int bri = 0, fri = batch.GlobinReactionCount;
      for (int r = 0; r < rawReactions.Length; r++)
      {
        if (rawReactions[r].TargetGlobinId >= 0) sortedReactions[bri++] = rawReactions[r];
        else sortedReactions[fri++] = rawReactions[r];
      }
      batch.Reactions = new NativeArray<MetabolicReactionProperties>(sortedReactions, Allocator.Persistent);

      for (int i = 0; i < template.Demands.Count; i++)
      {
        var demand = template.Demands[i];
        demand.Properties.GasId = gasRegistry.GetGasFromFormula(demand.Formula).Id;
        template.Demands[i] = demand;
      }

      for (int i = 0; i < template.ExcretableGases.Count; i++)
      {
        var excretion = template.ExcretableGases[i];
        excretion.GasId = gasRegistry.GetGasFromFormula(excretion.Formula).Id;
        template.ExcretableGases[i] = excretion;
      }

      batch.Template = template;
      batch.GasCount = gasRegistry.GasCount;
      batch.Settings = template.Properties;

      batch.TotalFreePlasma = new NativeArray<long>(batch.Capacity, Allocator.Persistent);
      batch.AllostericSums = new NativeArray<float>(batch.Capacity, Allocator.Persistent);
      batch.BohrShifts = new NativeArray<float>(batch.Capacity, Allocator.Persistent);

      // Initialize Danger Criteria
      batch.DangerCriteria = MetabolismDangerCriteria.Default();
      batch.DangerCriteria.MinTemp = template.MinTemperature;
      batch.DangerCriteria.MaxTemp = template.MaxTemperature;
      batch.DangerCriteria.MinTempFullDanger = template.MinTemperatureFullDanger;
      batch.DangerCriteria.MaxTempFullDanger = template.MaxTemperatureFullDanger;
      batch.DangerCriteria.MinPressure = template.MinPressureKpa;
      batch.DangerCriteria.MaxPressure = template.MaxPressureKpa;
      batch.DangerCriteria.MinPressureFullDanger = template.MinPressureFullDangerKpa;
      batch.DangerCriteria.MaxPressureFullDanger = template.MaxPressureFullDangerKpa;
      batch.DangerCriteria.RadiationResistance = template.RadiationResistance;
      batch.DangerCriteria.EnthalpySensitivity = template.EnthalpySensitivity;
      batch.DangerCriteria.ReferenceBodyTempK = template.ReferenceBodyTempK;
      batch.DangerCriteria.CausticResistance = template.CausticResistance;
      batch.DangerCriteria.RadiationSensitivity = template.RadiationSensitivity;
      batch.DangerCriteria.CausticSensitivity = template.CausticSensitivity;

      batch.GasDangerThresholds = new NativeArray<GasDangerThreshold>(batch.GasCount, Allocator.Persistent);
      for (int g = 0; g < batch.GasCount; g++) batch.GasDangerThresholds[g] = GasDangerThreshold.Safe();

      // 1. Derive from GasReaction if available
      if (template.GasReaction != null)
      {
        foreach (var reaction in template.GasReaction.Reactions)
        {
          if (reaction.Stages == null || reaction.Stages.Count == 0) continue;

          var gas = gasRegistry.GetGasFromFormula(reaction.Formula);
          if (gas == null) continue;

          float minPct = float.MaxValue;
          float maxPct = float.MinValue;
          foreach (var stage in reaction.Stages)
          {
            minPct = math.min(minPct, stage.MinPercentage);
            maxPct = math.max(maxPct, stage.MinPercentage);
          }

          var threshold = batch.GasDangerThresholds[gas.Id];
          if (reaction.IsAbsence)
          {
            // Absence logic handled via overrides or defaults
          }
          else
          {
            if (threshold.MaxFraction > 1.0f) threshold.MaxFraction = minPct;
            else threshold.MaxFraction = math.min(threshold.MaxFraction, minPct);

            if (threshold.MaxFullDangerFraction > 1.0f) threshold.MaxFullDangerFraction = maxPct;
            else threshold.MaxFullDangerFraction = math.min(threshold.MaxFullDangerFraction, maxPct);

            if (threshold.MaxFullDangerFraction <= threshold.MaxFraction)
            {
              threshold.MaxFullDangerFraction = threshold.MaxFraction + 0.01f;
            }
          }
          batch.GasDangerThresholds[gas.Id] = threshold;
        }
      }

      // 2. Apply XML manual overrides
      foreach (var xmlThreshold in template.GasDangerThresholds)
      {
        var gas = gasRegistry.GetGasFromFormula(xmlThreshold.Formula);
        if (gas == null) continue;

        var threshold = batch.GasDangerThresholds[gas.Id];
        if (xmlThreshold.MinFraction >= 0) threshold.MinFraction = xmlThreshold.MinFraction;
        if (xmlThreshold.MinFullDangerFraction >= 0) threshold.MinFullDangerFraction = xmlThreshold.MinFullDangerFraction;
        if (xmlThreshold.MaxFraction < 2.0f) threshold.MaxFraction = xmlThreshold.MaxFraction;
        if (xmlThreshold.MaxFullDangerFraction < 2.0f) threshold.MaxFullDangerFraction = xmlThreshold.MaxFullDangerFraction;
        batch.GasDangerThresholds[gas.Id] = threshold;
      }

      // 4. Calculate Masks for Sparse Scanning
      batch.RelevantGasMask = 0;
      batch.RequiredGasMask = 0;

      foreach (var demand in template.Demands)
      {
        var gas = gasRegistry.GetGasFromFormula(demand.Formula);
        if (gas != null)
        {
          batch.RelevantGasMask |= (1UL << gas.Id);
          batch.RequiredGasMask |= (1UL << gas.Id);

          var t = batch.GasDangerThresholds[gas.Id];
          if (t.MinFraction < 0)
          {
            t.MinFraction = 0.05f;
            t.MinFullDangerFraction = 0.01f;
            batch.GasDangerThresholds[gas.Id] = t;
          }
        }
      }

      for (int g = 0; g < batch.GasCount; g++)
      {
        var t = batch.GasDangerThresholds[g];
        bool isInteresting = (t.MinFraction >= 0f && t.MinFraction < 1.0f) || (t.MaxFraction >= 0f && t.MaxFraction < 1.0f);
        if (isInteresting)
        {
          batch.RelevantGasMask |= (1UL << g);
          if (t.MinFraction > 0f) batch.RequiredGasMask |= (1UL << g);
        }
      }

      if (template.GasReaction != null)
      {
        foreach (var reaction in template.GasReaction.Reactions)
        {
          if (reaction.IsAbsence)
          {
            var gas = gasRegistry.GetGasFromFormula(reaction.Formula);
            if (gas != null)
            {
              batch.RelevantGasMask |= (1UL << gas.Id);
              batch.RequiredGasMask |= (1UL << gas.Id);

              var t = batch.GasDangerThresholds[gas.Id];
              if (t.MinFraction < 0)
              {
                t.MinFraction = 0.05f;
                t.MinFullDangerFraction = 0.01f;
                batch.GasDangerThresholds[gas.Id] = t;
              }
            }
          }
        }
      }

      if (batch.GlobinCount > 0)
      {
        float hardness = batch.GlobinMetalHardnesses[0];
        batch.Settings.AllostericSensitivity = batch.Settings.AllostericSensitivity * (1.0f - hardness);
      }
      else
      {
        batch.Settings.AllostericSensitivity = 0f;
      }

      batch.InvPlasmaCapacity = 1.0f / math.max(1f, (float)batch.Settings.PlasmaCapacityUMol);
      batch.GasRadioactivities = new NativeArray<float>(batch.GasCount, Allocator.Persistent);
      NativeArray<float>.Copy(gasRegistry.GasData.Radioactivity, batch.GasRadioactivities);

      batch.Solubilities = new NativeArray<float>(batch.GasCount, Allocator.Persistent);
      batch.GasAffinities = new NativeArray<float>(batch.GasCount, Allocator.Persistent);
      batch.GasToGlobinIndex = new NativeArray<int>(batch.GasCount, Allocator.Persistent);
      batch.GasGlobinAffinities = new NativeArray<float>(batch.GasCount, Allocator.Persistent);

      for (int g = 0; g < batch.GasCount; g++) batch.GasToGlobinIndex[g] = -1;

      foreach (var gas in gasRegistry.AllGases)
      {
        batch.Solubilities[gas.Id] = gas.Properties.PlasmaSolubility;
        batch.GasAffinities[gas.Id] = gas.Properties.ChemicalAffinity;

        for (int b = 0; b < batch.GlobinCount; b++)
        {
          if (MatchesGlobin(gas, template.GlobinLevels[b], atomicRegistry))
          {
            batch.GasToGlobinIndex[gas.Id] = b;
            batch.GasGlobinAffinities[gas.Id] = template.GlobinLevels[b].BaseAffinity * gas.Properties.ChemicalAffinity;
            break;
          }
        }
      }

      var profile = template.AllostericProfile;
      batch.GasStructuralWarpingPotentials = new NativeArray<float>(batch.GasCount, Allocator.Persistent);
      for (int g = 0; g < batch.GasCount; g++)
      {
        var gas = gasRegistry.AllGases[g];
        var props = gas.Properties;

        if (batch.GasToGlobinIndex[g] >= 0 || profile.ExemptGases.Contains(gas.ChemicalFormula))
        {
          batch.GasStructuralWarpingPotentials[g] = 0f;
          continue;
        }

        float enFactor = math.max(0f, props.MeanElectronegativity - profile.ElectronegativityThreshold);
        float potency = (enFactor * 1.5f) + (props.DipoleMoment_Debye * profile.DipoleSensitivity);
        batch.GasStructuralWarpingPotentials[g] = potency;
      }

      int numGlobinGases = 0, numFreeGases = 0;
      for (int g = 0; g < batch.GasCount; g++)
      {
        if (batch.GasToGlobinIndex[g] >= 0) numGlobinGases++;
        else numFreeGases++;
      }

      batch.GlobinGasIds = new NativeArray<int>(numGlobinGases, Allocator.Persistent);
      batch.GlobinGasGlobinIds = new NativeArray<int>(numGlobinGases, Allocator.Persistent);
      batch.GlobinGasBindAffinities = new NativeArray<float>(numGlobinGases, Allocator.Persistent);
      batch.GlobinHillCoeffs = new NativeArray<float>(numGlobinGases, Allocator.Persistent);
      batch.GlobinP50Fracs = new NativeArray<float>(numGlobinGases, Allocator.Persistent);
      batch.GlobinGasLungUptakeFactors = new NativeArray<float>(numGlobinGases, Allocator.Persistent);
      batch.GlobinGasLungReleaseFactors = new NativeArray<float>(numGlobinGases, Allocator.Persistent);

      batch.FreeGasIds = new NativeArray<int>(numFreeGases, Allocator.Persistent);
      batch.FreeGasUptakeFactors = new NativeArray<float>(numFreeGases, Allocator.Persistent);
      batch.FreeGasReleaseFactors = new NativeArray<float>(numFreeGases, Allocator.Persistent);

      float volRatio = (float)batch.Settings.PlasmaCapacityUMol / math.max(1f, (float)batch.Settings.LungCapacityUMol);

      int bgi = 0, fgi = 0;
      foreach (var gas in gasRegistry.AllGases)
      {
        int g = gas.Id;
        int slot = batch.GasToGlobinIndex[g];
        if (slot >= 0)
        {
          batch.GlobinGasIds[bgi] = g;
          batch.GlobinGasGlobinIds[bgi] = slot;
          batch.GlobinGasBindAffinities[bgi] = batch.GasGlobinAffinities[g];
          batch.GlobinHillCoeffs[bgi] = batch.GlobinSpecs[slot].HillCoefficient;
          batch.GlobinP50Fracs[bgi] = batch.GlobinSpecs[slot].P50LungFraction;

          float sol = gas.Properties.PlasmaSolubility * 0.0001f;
          float aff = math.max(gas.Properties.ChemicalAffinity, 0.001f);
          batch.GlobinGasLungUptakeFactors[bgi] = volRatio * sol * aff;
          batch.GlobinGasLungReleaseFactors[bgi] = aff;
          bgi++;
        }
        else
        {
          float sol = gas.Properties.PlasmaSolubility * 0.0001f;
          float aff = math.max(gas.Properties.ChemicalAffinity, 0.001f);
          batch.FreeGasIds[fgi] = g;
          batch.FreeGasUptakeFactors[fgi] = volRatio * sol * aff;
          batch.FreeGasReleaseFactors[fgi] = aff;
          fgi++;
        }
      }

      batch.ExcretedEfficiencies = new NativeArray<long>(batch.GasCount, Allocator.Persistent);
      foreach (var eff in template.ExcretableGases)
      {
        if (eff.GasId >= 0 && eff.GasId < batch.GasCount)
          batch.ExcretedEfficiencies[eff.GasId] = eff.EfficiencyBP;
      }

      batch.PlasmaStorage = new NativeArray<long>(batch.Capacity * batch.GasCount, Allocator.Persistent);
      batch.LungStorage = new NativeArray<long>(batch.Capacity * batch.GasCount, Allocator.Persistent);
      batch.GlobinLevels = new NativeArray<GlobinState>(batch.Capacity * batch.GlobinCount, Allocator.Persistent);
      batch.EntityLungNetFlux = new NativeArray<long>(batch.Capacity * batch.GasCount, Allocator.Persistent);
      batch.WorldIndices = new NativeArray<int>(batch.Capacity, Allocator.Persistent);
      batch.EntitySolubilityScale = new NativeArray<float>(batch.Capacity, Allocator.Persistent);

      for (int idx = 0; idx < batch.Capacity * batch.GlobinCount; idx++)
        batch.GlobinLevels[idx] = new GlobinState { OccupantGasId = -1 };
      for (int idx = 0; idx < batch.Capacity; idx++)
        batch.EntitySolubilityScale[idx] = 1.0f;

      batch.EntityLungEfficiency = new NativeArray<float>(batch.Capacity, Allocator.Persistent);
      batch.EntityPlasmaExchangeEfficiency = new NativeArray<float>(batch.Capacity, Allocator.Persistent);
      batch.EntityPlasmaFiltrationEfficiency = new NativeArray<float>(batch.Capacity, Allocator.Persistent);
      for (int idx = 0; idx < batch.Capacity; idx++)
      {
        batch.EntityLungEfficiency[idx] = 1.0f;
        batch.EntityPlasmaExchangeEfficiency[idx] = 1.0f;
        batch.EntityPlasmaFiltrationEfficiency[idx] = 1.0f;
      }

      batch.ExternalGasSource = new IGasProvider?[batch.Capacity];
      batch.HasExternalSupply = new NativeArray<bool>(batch.Capacity, Allocator.Persistent);
      batch.ExternalLungSupply = new NativeArray<long>(batch.Capacity * batch.GasCount, Allocator.Persistent);
      batch.ExternalNetConsumed = new NativeArray<long>(batch.Capacity * batch.GasCount, Allocator.Persistent);
      batch.Metabolizers = new IMetabolizer?[batch.Capacity];
    }

    private static bool MatchesGlobin(GasDefinition gas, StorageGlobinCriteria criteria, AtomicRegistry atomicRegistry)
    {
      var props = gas.Properties;
      float gasDiameterPm = props.CollisionDiameterAngstroms * 100f;
      if (gasDiameterPm > criteria.DerivedMaxCollisionDiameter) return false;
      if (props.MeanElectronegativity < criteria.DerivedMinElectronegativity) return false;
      var components = Formulas.ParseFormula(gas.ChemicalFormula, atomicRegistry);
      foreach (var symbol in components.Keys)
      {
        if (AtomPhysicsAnalyzer.CanFormCoordinateBond(atomicRegistry, symbol, criteria.MetalSymbol.ToString()))
          return true;
      }
      return false;
    }

    private static int FindGlobinIndex(string globinName, GasMetabolism template)
    {
      for (int i = 0; i < template.GlobinLevels.Count; i++)
      {
        if (template.GlobinLevels[i].Name.ToString() == globinName) return i;
      }
      return -1;
    }
  }
}
