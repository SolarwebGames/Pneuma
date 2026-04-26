using System;
using Unity.Collections;
using Unity.Mathematics;
using SolarWeb.Pneuma.Gas;
using SolarWeb.Pneuma.Atoms;
using SolarWeb.Pneuma.Data;
using SolarWeb.Pneuma.GasExchange;

namespace SolarWeb.Pneuma.Metabolism
{
  public class SimplifiedMetabolismBatch : IMetabolismBatch
  {
    public int MetabolismId { get; }
    public int DangerBufferIndex { get; set; }
    public int Count { get; private set; }
    public int Capacity { get; private set; }
    public bool IsSimplified => true;

    private readonly MetabolismState state;
    private readonly GasRegistry gasRegistry;
    private readonly ExchangerBuffer exchangerBuffer;

    // --- Precomputed Species-Wide Weights ---
    public NativeArray<float> GlobalToxWeights;
    public NativeArray<float> GlobalCausticWeights;
    public NativeArray<float> GlobalBioInterferenceWeights;
    public NativeArray<float> GlobalRadWeights;
    public int VitalGasId = -1;
    public float LethalThreshold = 0.10f; // 10% partial pressure fallback
    public float SuffocationPenalty = 0.05f;
    public float RecoveryRate = 0.002f; // integrity/s recovered when vitals OK and stress == 0

    public float RadiationSensitivity = 2.0f;
    public float CausticSensitivity = 1.0f;

    public float MaxPressureKpa = 200f;
    public float MaxPressureFullDangerKpa = 500f;

    public float MinPressureKpa = 50f;
    public float MinPressureFullDangerKpa = 20f;

    // --- Gas Exchange ---
    /// <summary>How often (in simulation ticks) this batch's job and exchanger slots are processed.</summary>
    public int SampleRate = 4;
    public long VitalConsumptionRateUMol;    // µmol/s
    public int ByproductGasId = -1;
    public long ByproductProductionRateUMol; // µmol/s
    /// <summary>Per-entity exchanger persistent IDs: [entityIdx * 2 + 0] = vital-gas slot, [entityIdx * 2 + 1] = byproduct slot.</summary>
    public NativeArray<int> ExchangerPersistentIds;

    // --- Per-Entity Structure of Arrays (SoA) ---
    public NativeArray<float> AnimalIntegrity;
    public NativeArray<float> AnimalToxicResistance;
    public NativeArray<float> AnimalCorrosiveResistance;
    public NativeArray<float> AnimalRadiationResistance;
    public NativeArray<float> AnimalPressureResistance;
    public NativeArray<float> AnimalVacuumResistance;
    public NativeArray<int> WorldIndices;
    public IMetabolizer?[] Metabolizers = null!;

    public SimplifiedMetabolismBatch(MetabolismState state, int batchIndex, GasMetabolism template, int initialCapacity, GasRegistry gasRegistry, AtomicRegistry atomicRegistry, ExchangerBuffer exchangerBuffer)
    {
      this.state = state;
      DangerBufferIndex = batchIndex;
      MetabolismId = template.Id;
      this.gasRegistry = gasRegistry;
      this.exchangerBuffer = exchangerBuffer;
      this.MetabolismId = template.Id;
      this.Capacity = initialCapacity;
      this.Count = 0;

      this.MaxPressureKpa = template.MaxPressureKpa;
      this.MaxPressureFullDangerKpa = template.MaxPressureFullDangerKpa;
      this.MinPressureKpa = template.MinPressureKpa;
      this.MinPressureFullDangerKpa = template.MinPressureFullDangerKpa;
      this.RadiationSensitivity = template.RadiationSensitivity;
      this.CausticSensitivity = template.CausticSensitivity;

      AnalyzeChemistry(template, gasRegistry, atomicRegistry);
      InitializeArrays();
    }

    private void AnalyzeChemistry(GasMetabolism template, GasRegistry gasRegistry, AtomicRegistry atomicRegistry)
    {
      int gasCount = gasRegistry.GasCount;
      GlobalToxWeights = new NativeArray<float>(gasCount, Allocator.Persistent);
      GlobalCausticWeights = new NativeArray<float>(gasCount, Allocator.Persistent);
      GlobalBioInterferenceWeights = new NativeArray<float>(gasCount, Allocator.Persistent);
      GlobalRadWeights = new NativeArray<float>(gasCount, Allocator.Persistent);

      // Identify Vital Gas and its consumption rate
      if (template.Demands.Count > 0)
      {
        var demand = template.Demands[0];
        var gas = gasRegistry.GetGasFromFormula(demand.Formula);
        if (gas != null)
        {
          VitalGasId = gas.Id;
          VitalConsumptionRateUMol = demand.Properties.RequiredUMolPerSecond;
        }
      }
      else if (template.Reactions.Count > 0)
      {
        var gas = gasRegistry.GetGasFromFormula(template.Reactions[0].InputFormula);
        if (gas != null) VitalGasId = gas.Id;
      }

      // Identify byproduct gas and its production rate from the first reaction
      if (template.Reactions.Count > 0)
      {
        var reaction = template.Reactions[0];
        var byproductGas = gasRegistry.GetGasFromFormula(reaction.OutputFormula);
        if (byproductGas != null)
        {
          ByproductGasId = byproductGas.Id;
          ByproductProductionRateUMol = reaction.Properties.TargetMetabolicRatePerSecond;
        }
      }

      float vitalPotential = VitalGasId >= 0 ? gasRegistry.GasData.StructuralWarpingPotential[VitalGasId] : 1.0f;

      for (int g = 0; g < gasCount; g++)
      {
        // 1. Toxicity: Higher structural warping potential than vital gas indicates inhibition/displacement
        float potential = gasRegistry.GasData.StructuralWarpingPotential[g];
        if (potential > vitalPotential)
        {
          GlobalToxWeights[g] = math.log10(potential / math.max(0.1f, vitalPotential)) * 0.5f;
        }

        // 2. Causticity: direct tissue corrosion (OxidizingPotency + halogen acid formation)
        GlobalCausticWeights[g] = gasRegistry.GasData.Corrosiveness[g];

        // 3b. Bio-interference: polar disruption via electrochemical deviation (acidic or basic)
        GlobalBioInterferenceWeights[g] = gasRegistry.GasData.BioInterference[g];

        // 3. Radioactivity: Directly from registry
        GlobalRadWeights[g] = gasRegistry.GasData.Radioactivity[g];
      }

      // Apply XML specific overrides if any
      foreach (var xmlThreshold in template.GasDangerThresholds)
      {
        var gas = gasRegistry.GetGasFromFormula(xmlThreshold.Formula);
        if (gas == null) continue;
        if (xmlThreshold.MaxFraction < 1.0f)
        {
          GlobalToxWeights[gas.Id] = math.max(GlobalToxWeights[gas.Id], 1.0f / math.max(0.01f, xmlThreshold.MaxFraction));
        }
      }

      // Zero out all stress weights for gases this biology treats as safe
      foreach (string formula in template.SafeGasFormulas)
      {
        var gas = gasRegistry.GetGasFromFormula(formula);
        if (gas == null) continue;
        GlobalToxWeights[gas.Id] = 0f;
        GlobalCausticWeights[gas.Id] = 0f;
        GlobalBioInterferenceWeights[gas.Id] = 0f;
        GlobalRadWeights[gas.Id] = 0f;
      }

      // Apply explicit toxicity overrides (replaces SWP-derived weight for that gas)
      foreach (var hazard in template.HazardOverrides)
      {
        var gas = gasRegistry.GetGasFromFormula(hazard.Formula);
        if (gas == null) continue;
        GlobalToxWeights[gas.Id] = hazard.Toxicity;
      }
    }

    private void InitializeArrays()
    {
      AnimalIntegrity = new NativeArray<float>(Capacity, Allocator.Persistent);
      AnimalToxicResistance = new NativeArray<float>(Capacity, Allocator.Persistent);
      AnimalCorrosiveResistance = new NativeArray<float>(Capacity, Allocator.Persistent);
      AnimalRadiationResistance = new NativeArray<float>(Capacity, Allocator.Persistent);
      AnimalPressureResistance = new NativeArray<float>(Capacity, Allocator.Persistent);
      AnimalVacuumResistance = new NativeArray<float>(Capacity, Allocator.Persistent);
      WorldIndices = new NativeArray<int>(Capacity, Allocator.Persistent);
      ExchangerPersistentIds = new NativeArray<int>(Capacity * 2, Allocator.Persistent);
      Metabolizers = new IMetabolizer?[Capacity];

      for (int i = 0; i < Capacity; i++) AnimalIntegrity[i] = 1.0f;
    }

    public int AddEntity(IMetabolizer metabolizer)
    {
      if (Count >= Capacity) Grow();
      int idx = Count++;
      WorldIndices[idx] = metabolizer.Position;
      Metabolizers[idx] = metabolizer;
      AnimalIntegrity[idx] = 1.0f;
      AnimalToxicResistance[idx] = 0f;
      AnimalCorrosiveResistance[idx] = 0f;
      AnimalRadiationResistance[idx] = 0f;
      AnimalPressureResistance[idx] = 0f;
      AnimalVacuumResistance[idx] = 0f;

      int o2PId = VitalGasId >= 0
        ? exchangerBuffer.Add(VitalGasId, -VitalConsumptionRateUMol, 0, metabolizer, SampleRate)
        : -1;
      int co2PId = ByproductGasId >= 0
        ? exchangerBuffer.Add(ByproductGasId, ByproductProductionRateUMol, 0, metabolizer, SampleRate)
        : -1;

      ExchangerPersistentIds[idx * 2 + 0] = o2PId;
      ExchangerPersistentIds[idx * 2 + 1] = co2PId;

      return idx;
    }

    public void RemoveEntity(int entityIdx)
    {
      if (entityIdx < 0 || entityIdx >= Count) return;

      // Remove exchanger slots for this entity
      int o2PId = ExchangerPersistentIds[entityIdx * 2 + 0];
      int co2PId = ExchangerPersistentIds[entityIdx * 2 + 1];
      if (o2PId >= 0) exchangerBuffer.Remove(o2PId);
      if (co2PId >= 0) exchangerBuffer.Remove(co2PId);

      int lastIdx = --Count;
      if (entityIdx != lastIdx)
      {
        AnimalIntegrity[entityIdx] = AnimalIntegrity[lastIdx];
        AnimalToxicResistance[entityIdx] = AnimalToxicResistance[lastIdx];
        AnimalCorrosiveResistance[entityIdx] = AnimalCorrosiveResistance[lastIdx];
        AnimalRadiationResistance[entityIdx] = AnimalRadiationResistance[lastIdx];
        AnimalPressureResistance[entityIdx] = AnimalPressureResistance[lastIdx];
        AnimalVacuumResistance[entityIdx] = AnimalVacuumResistance[lastIdx];
        WorldIndices[entityIdx] = WorldIndices[lastIdx];
        ExchangerPersistentIds[entityIdx * 2 + 0] = ExchangerPersistentIds[lastIdx * 2 + 0];
        ExchangerPersistentIds[entityIdx * 2 + 1] = ExchangerPersistentIds[lastIdx * 2 + 1];
        Metabolizers[entityIdx] = Metabolizers[lastIdx];
        if (Metabolizers[entityIdx] != null) Metabolizers[entityIdx]!.BatchIndex = entityIdx;
      }
      Metabolizers[lastIdx] = null;
    }

    public void UpdatePosition(IMetabolizer metabolizer)
    {
      int idx = metabolizer.BatchIndex;
      if (idx < 0 || idx >= Count) return;

      WorldIndices[idx] = metabolizer.Position;

      int o2PId = ExchangerPersistentIds[idx * 2 + 0];
      int co2PId = ExchangerPersistentIds[idx * 2 + 1];

      if (o2PId >= 0)
      {
        int internalId = exchangerBuffer.GlobalIndexLookup[o2PId];
        if (internalId >= 0)
        {
          exchangerBuffer.ExchangerWorldIndices[internalId] = metabolizer.Position;
          exchangerBuffer.ExchangerRegionIndices[internalId] = metabolizer.Region;
        }
      }
      if (co2PId >= 0)
      {
        int internalId = exchangerBuffer.GlobalIndexLookup[co2PId];
        if (internalId >= 0)
        {
          exchangerBuffer.ExchangerWorldIndices[internalId] = metabolizer.Position;
          exchangerBuffer.ExchangerRegionIndices[internalId] = metabolizer.Region;
        }
      }

      exchangerBuffer.IsDirty = true;
    }

    public void UpdateEfficiencies(int entityIdx, float breathing, float pumping, float filtration)
    {
      // Animals currently don't use these in the simplified job,
      // but we could use them to scale the suffocation penalty if needed.
    }

    public void UpdateResistances(int entityIdx, float toxic, float corrosive, float radiation, float pressure, float vacuum)
    {
      if (entityIdx >= 0 && entityIdx < Count)
      {
        AnimalToxicResistance[entityIdx] = toxic;
        AnimalCorrosiveResistance[entityIdx] = corrosive;
        AnimalRadiationResistance[entityIdx] = radiation;
        AnimalPressureResistance[entityIdx] = pressure;
        AnimalVacuumResistance[entityIdx] = vacuum;
      }
    }

    public float GetDangerLevel(int regionIdx, int simIdx = -1)
    {
      // Animals currently don't report danger back to the grid in the same way,
      // but we could implement a simplified version of GetDangerLevel if needed.
      return 0;
    }

    public System.Xml.Linq.XElement CaptureSnapshot(System.Func<int, string> getGasName)
    {
      var root = new System.Xml.Linq.XElement("SimulationSnapshot",
        new System.Xml.Linq.XAttribute("metabolismId", MetabolismId),
        new System.Xml.Linq.XAttribute("isSimplified", true),
        new System.Xml.Linq.XAttribute("vitalGasId", VitalGasId),
        new System.Xml.Linq.XAttribute("vitalGasName", VitalGasId >= 0 ? getGasName(VitalGasId) : "None"),
        new System.Xml.Linq.XAttribute("lethalThreshold", LethalThreshold.ToString("R")),
        new System.Xml.Linq.XAttribute("suffocationPenalty", SuffocationPenalty.ToString("R")),
        new System.Xml.Linq.XAttribute("sampleRate", SampleRate),
        new System.Xml.Linq.XAttribute("vitalConsumptionRateUMol", VitalConsumptionRateUMol),
        new System.Xml.Linq.XAttribute("byproductGasId", ByproductGasId),
        new System.Xml.Linq.XAttribute("byproductGasName", ByproductGasId >= 0 ? getGasName(ByproductGasId) : "None"),
        new System.Xml.Linq.XAttribute("byproductProductionRateUMol", ByproductProductionRateUMol)
      );

      var weights = new System.Xml.Linq.XElement("Weights");
      int gasCount = GlobalToxWeights.Length;
      for (int g = 0; g < gasCount; g++)
      {
        float tw = GlobalToxWeights[g];
        float cw = GlobalCausticWeights[g];
        float rw = GlobalRadWeights[g];

        float bw = GlobalBioInterferenceWeights[g];
        if (tw > 0 || cw > 0 || bw > 0 || rw > 0)
        {
          weights.Add(new System.Xml.Linq.XElement("Gas",
            new System.Xml.Linq.XAttribute("id", g),
            new System.Xml.Linq.XAttribute("name", getGasName(g)),
            new System.Xml.Linq.XAttribute("tox", tw.ToString("R")),
            new System.Xml.Linq.XAttribute("caustic", cw.ToString("R")),
            new System.Xml.Linq.XAttribute("bio", bw.ToString("R")),
            new System.Xml.Linq.XAttribute("rad", rw.ToString("R"))
          ));
        }
      }
      root.Add(weights);

      return root;
    }

    private void Grow()
    {
      int newCap = Capacity * 2;
      Resize(ref AnimalIntegrity, newCap);
      Resize(ref AnimalToxicResistance, newCap);
      Resize(ref AnimalCorrosiveResistance, newCap);
      Resize(ref AnimalRadiationResistance, newCap);
      Resize(ref AnimalPressureResistance, newCap);
      Resize(ref AnimalVacuumResistance, newCap);
      Resize(ref WorldIndices, newCap);
      ResizeExchangerIds(ref ExchangerPersistentIds, newCap);
      Array.Resize(ref Metabolizers, newCap);
      Capacity = newCap;
    }

    private void Resize<T>(ref NativeArray<T> array, int newSize) where T : struct
    {
      var newArray = new NativeArray<T>(newSize, Allocator.Persistent);
      if (array.IsCreated)
      {
        NativeArray<T>.Copy(array, newArray, math.min(array.Length, newSize));
        array.Dispose();
      }
      array = newArray;
    }

    private void ResizeExchangerIds(ref NativeArray<int> array, int newEntityCapacity)
    {
      var newArray = new NativeArray<int>(newEntityCapacity * 2, Allocator.Persistent);
      if (array.IsCreated)
      {
        NativeArray<int>.Copy(array, newArray, math.min(array.Length, newArray.Length));
        array.Dispose();
      }
      array = newArray;
    }

    public void Dispose()
    {
      GlobalToxWeights.SafeDispose();
      GlobalCausticWeights.SafeDispose();
      GlobalBioInterferenceWeights.SafeDispose();
      GlobalRadWeights.SafeDispose();
      AnimalIntegrity.SafeDispose();
      AnimalToxicResistance.SafeDispose();
      AnimalCorrosiveResistance.SafeDispose();
      AnimalRadiationResistance.SafeDispose();
      AnimalPressureResistance.SafeDispose();
      AnimalVacuumResistance.SafeDispose();
      WorldIndices.SafeDispose();
      ExchangerPersistentIds.SafeDispose();
    }
  }
}
