using System;
using System.Xml.Linq;
using Unity.Collections;
using Unity.Mathematics;
using SolarWeb.Pneuma.GasExchange;
using SolarWeb.Pneuma.Gas;
using SolarWeb.Pneuma.Atoms;
using SolarWeb.Pneuma.Data;

namespace SolarWeb.Pneuma.Metabolism
{
  public class MetabolismBatch : IMetabolismBatch
  {
    private readonly int metabolismId;
    public int MetabolismId => metabolismId;
    public MetabolismState State;
    public int DangerBufferIndex { get; set; }
    public int Count { get; private set; }
    public int Capacity;
    public int GasCount;
    public int GlobinCount;
    public bool IsSimplified => false;

    public const int BLOCK_SIZE = 8;

    // Reusable scratch buffer for bulk provider snapshots — avoids per-gas virtual dispatch in DoExternalProviderPreStep.
    public long[]? ProviderSnapshotBuffer;

    public ExchangerBuffer ExchangerBuffer;
    public GasMetabolism Template = null!;
    public MetabolismProperties Settings;
    public MetabolismDangerCriteria DangerCriteria;
    public NativeArray<GasDangerThreshold> GasDangerThresholds;
    public ulong RelevantGasMask;
    public ulong RequiredGasMask;
    public float InvPlasmaCapacity; // 1.0f / PlasmaCapacityUMol, precomputed
    public NativeArray<float> GasStructuralWarpingPotentials;
    public NativeArray<float> GasRadioactivities;

    // --- Pre-computed state for jobs ---
    public NativeArray<long> TotalFreePlasma;
    public NativeArray<float> AllostericSums;
    public NativeArray<float> BohrShifts;

    // --- Globin specs and gas-to-globin mapping ---
    public NativeArray<StorageGlobinCriteria> GlobinSpecs;
    public NativeArray<float> GlobinMaxCollisionDiameters;
    public NativeArray<float> GlobinMinElectronegativities;
    public NativeArray<float> GlobinMetalHardnesses;
    public NativeArray<long> GlobinCapacities;      // [GlobinIdx] = CapacityUMol [μmol]
    public NativeArray<float> InvGlobinCapacities;   // [GlobinIdx] = 1.0f / CapacityUMol
    public NativeArray<int> GasToGlobinIndex;      // [GasId] → globin index, or -1

    // --- Parallel arrays for globin-assigned gases (job-optimised layout) ---
    public NativeArray<int> GlobinGasIds;           // [bi] = gas ID
    public NativeArray<int> GlobinGasGlobinIds;     // [bi] = globin slot index
    public NativeArray<float> GlobinGasBindAffinities;// [bi] = BaseAffinity * ChemicalAffinity
    public NativeArray<float> GlobinHillCoeffs;       // [bi] = HillCoefficient for this globin
    public NativeArray<float> GlobinP50Fracs;         // [bi] = P50LungFraction for this globin
    public NativeArray<float> GlobinGasLungUptakeFactors;   // [bi] = volRatio * solubility * 0.0001
    public NativeArray<float> GlobinGasLungReleaseFactors;  // [bi] = 1.0f (no affinity for plasma step)

    // --- Parallel arrays for freely-dissolved gases (job-optimised layout) ---
    public NativeArray<int> FreeGasIds;             // [fi] = gas ID
    public NativeArray<float> FreeGasUptakeFactors;   // [fi] = solubility * 0.0001f * affinity
    public NativeArray<float> FreeGasReleaseFactors;  // [fi] = solubility * 0.0001f / affinity

    // --- Reactions ---
    public NativeArray<MetabolicReactionProperties> Reactions;
    public int GlobinReactionCount; // Reactions[0..GlobinReactionCount) are globin-sourced

    public NativeArray<float> Solubilities;
    public NativeArray<float> GasAffinities;
    public NativeArray<float> GasGlobinAffinities;
    public NativeArray<long> ExcretedEfficiencies;

    // --- Per-entity storage ---
    public NativeArray<long> LungStorage;
    public NativeArray<long> PlasmaStorage;
    public NativeArray<GlobinState> GlobinLevels; // [EntityIdx * GlobinCount + GlobinIdx]
    public NativeArray<long> EntityLungNetFlux;
    public NativeArray<int> WorldIndices;
    public NativeArray<float> EntitySolubilityScale;

    // --- Per-entity health efficiency scalars ---
    public NativeArray<float> EntityLungEfficiency;
    public NativeArray<float> EntityPlasmaExchangeEfficiency;
    public NativeArray<float> EntityPlasmaFiltrationEfficiency;

    public IGasProvider?[] ExternalGasSource = null!;
    public NativeArray<bool> HasExternalSupply;     // [entityIdx]
    public NativeArray<long> ExternalLungSupply;    // [entityIdx * GasCount + gasId]
    public NativeArray<long> ExternalNetConsumed;   // [entityIdx * GasCount + gasId]
    public IMetabolizer?[] Metabolizers = null!;

    // ── Threshold-subscription system ────────────────────────────────────────
    public int SubscriptionCount;
    public NativeArray<int> SubGasIds;
    public NativeArray<float> SubThresholds;
    public NativeArray<float> SubHysteresis;
    public NativeArray<bool> SubIsExcess;
    public NativeArray<bool> SubIsPlasma; // true=plasma fraction, false=lung fraction

    public NativeArray<bool> StageActive;
    public NativeArray<bool> StageJustTriggered;
    public NativeArray<bool> StageJustReset;

    public MetabolismBatch(MetabolismState state, int dangerBufferIndex, ExchangerBuffer exchangerBuffer, GasMetabolism template, int initialCapacity, GasRegistry gasRegistry, AtomicRegistry atomicRegistry)
    {
      State = state;
      DangerBufferIndex = dangerBufferIndex;
      ExchangerBuffer = exchangerBuffer;
      metabolismId = template.Id;
      Capacity = (initialCapacity + BLOCK_SIZE - 1) & ~(BLOCK_SIZE - 1);
      Count = 0;

      MetabolismBatchBuilder.InitializeBatch(this, template, gasRegistry, atomicRegistry);
    }

    public float GetDangerLevel(int regionIdx, int simIdx = -1)
    {
      if (!State.DangerLevels.IsCreated || regionIdx < 0) return 0;
      if (simIdx >= 0 && simIdx < State.CurrentCellStride)
      {
        if (State.CellDangerLevels.IsCreated)
        {
          float cellDanger = State.CellDangerLevels[DangerBufferIndex * State.CurrentCellStride + simIdx];
          if (cellDanger > 0) return cellDanger;
        }
      }
      int stride = State.CurrentRegionStride;
      if (regionIdx >= stride) return 0;
      return State.DangerLevels[DangerBufferIndex * stride + regionIdx];
    }

    public void RegisterProvider(int batchIndex, IGasProvider provider)
    {
      if (batchIndex >= 0 && batchIndex < Count)
        ExternalGasSource[batchIndex] = provider;
    }

    public void DeregisterProvider(int batchIndex)
    {
      if (batchIndex >= 0 && batchIndex < Count)
        ExternalGasSource[batchIndex] = null;
    }

    public int AddEntity(IMetabolizer metabolizer)
    {
      if (Count >= Capacity) Grow();
      int entityIdx = Count;
      WorldIndices[entityIdx] = metabolizer.Position;
      Metabolizers[entityIdx] = metabolizer;
      ExternalGasSource[entityIdx] = null;
      for (int g = 0; g < GasCount; g++) PlasmaStorage[GetGasIndex(entityIdx, g)] = 0;
      for (int b = 0; b < GlobinCount; b++) GlobinLevels[GetGlobinIndex(entityIdx, b)] = new GlobinState { OccupantGasId = -1 };
      EntityLungEfficiency[entityIdx] = 1.0f;
      EntityPlasmaExchangeEfficiency[entityIdx] = 1.0f;
      EntityPlasmaFiltrationEfficiency[entityIdx] = 1.0f;
      TotalFreePlasma[entityIdx] = 0;
      AllostericSums[entityIdx] = 0f;
      BohrShifts[entityIdx] = 1.0f;
      Count++;
      return entityIdx;
    }

    public void RemoveEntity(int entityIdx)
    {
      if (entityIdx < 0 || entityIdx >= Count) return;
      int lastIdx = Count - 1;
      if (entityIdx != lastIdx)
      {
        WorldIndices[entityIdx] = WorldIndices[lastIdx];
        ExternalGasSource[entityIdx] = ExternalGasSource[lastIdx];
        Metabolizers[entityIdx] = Metabolizers[lastIdx];
        if (Metabolizers[entityIdx] != null) Metabolizers[entityIdx]!.BatchIndex = entityIdx;
        EntitySolubilityScale[entityIdx] = EntitySolubilityScale[lastIdx];
        TotalFreePlasma[entityIdx] = TotalFreePlasma[lastIdx];
        AllostericSums[entityIdx] = AllostericSums[lastIdx];
        BohrShifts[entityIdx] = BohrShifts[lastIdx];
        EntityLungEfficiency[entityIdx] = EntityLungEfficiency[lastIdx];
        EntityPlasmaExchangeEfficiency[entityIdx] = EntityPlasmaExchangeEfficiency[lastIdx];
        EntityPlasmaFiltrationEfficiency[entityIdx] = EntityPlasmaFiltrationEfficiency[lastIdx];
        for (int g = 0; g < GasCount; g++)
        {
          PlasmaStorage[GetGasIndex(entityIdx, g)] = PlasmaStorage[GetGasIndex(lastIdx, g)];
          LungStorage[GetGasIndex(entityIdx, g)] = LungStorage[GetGasIndex(lastIdx, g)];
          EntityLungNetFlux[GetGasIndex(entityIdx, g)] = EntityLungNetFlux[GetGasIndex(lastIdx, g)];
        }
        for (int b = 0; b < GlobinCount; b++)
          GlobinLevels[GetGlobinIndex(entityIdx, b)] = GlobinLevels[GetGlobinIndex(lastIdx, b)];
      }
      ExternalGasSource[lastIdx] = null;
      Metabolizers[lastIdx] = null;
      Count--;
    }

    public void UpdatePosition(IMetabolizer metabolizer)
    {
      WorldIndices[metabolizer.BatchIndex] = metabolizer.Position;
    }

    public void UpdateEfficiencies(int entityIdx, float breathing, float pumping, float filtration)
    {
      if (entityIdx >= 0 && entityIdx < Count)
      {
        EntityLungEfficiency[entityIdx] = breathing;
        EntityPlasmaExchangeEfficiency[entityIdx] = pumping;
        EntityPlasmaFiltrationEfficiency[entityIdx] = filtration;
      }
    }

    public XElement CaptureSnapshot(Func<int, string> getGasName)
    {
      return MetabolismBatchSerializer.CaptureSnapshot(this, getGasName);
    }

    public void UpdateResistances(int entityIdx, float toxic, float corrosive, float radiation, float pressure, float vacuum) { }

    public void SetSubscriptions(int[] gasIds, float[] thresholds, float[] hystereses, bool[] isExcess, bool[] isPlasma)
    {
      SubGasIds.SafeDispose(); SubThresholds.SafeDispose(); SubHysteresis.SafeDispose(); SubIsExcess.SafeDispose(); SubIsPlasma.SafeDispose();
      StageActive.SafeDispose(); StageJustTriggered.SafeDispose(); StageJustReset.SafeDispose();
      SubscriptionCount = gasIds.Length;
      SubGasIds = new NativeArray<int>(gasIds, Allocator.Persistent);
      SubThresholds = new NativeArray<float>(thresholds, Allocator.Persistent);
      SubHysteresis = new NativeArray<float>(hystereses, Allocator.Persistent);
      SubIsExcess = new NativeArray<bool>(isExcess, Allocator.Persistent);
      SubIsPlasma = new NativeArray<bool>(isPlasma, Allocator.Persistent);
      int total = Capacity * SubscriptionCount;
      StageActive = new NativeArray<bool>(total, Allocator.Persistent);
      StageJustTriggered = new NativeArray<bool>(total, Allocator.Persistent);
      StageJustReset = new NativeArray<bool>(total, Allocator.Persistent);
    }

    private void Grow()
    {
      int newCapacity = Capacity * 2; int oldCapacity = Capacity;
      ResizeLinear(ref WorldIndices, oldCapacity, newCapacity);
      ResizeLinear(ref TotalFreePlasma, oldCapacity, newCapacity);
      ResizeLinear(ref AllostericSums, oldCapacity, newCapacity);
      ResizeLinear(ref BohrShifts, oldCapacity, newCapacity);
      ResizeLinear(ref EntitySolubilityScale, oldCapacity, newCapacity);
      ResizeLinear(ref EntityLungEfficiency, oldCapacity, newCapacity);
      ResizeLinear(ref EntityPlasmaExchangeEfficiency, oldCapacity, newCapacity);
      ResizeLinear(ref EntityPlasmaFiltrationEfficiency, oldCapacity, newCapacity);
      ResizeContiguousGases(ref PlasmaStorage, oldCapacity, newCapacity);
      ResizeContiguousGases(ref LungStorage, oldCapacity, newCapacity);
      ResizeContiguousGases(ref EntityLungNetFlux, oldCapacity, newCapacity);
      ResizeContiguousGlobins(ref GlobinLevels, oldCapacity, newCapacity);
      if (SubscriptionCount > 0)
      {
        ResizeContiguousSubs(ref StageActive, oldCapacity, newCapacity);
        ResizeContiguousSubs(ref StageJustTriggered, oldCapacity, newCapacity);
        ResizeContiguousSubs(ref StageJustReset, oldCapacity, newCapacity);
      }
      Array.Resize(ref ExternalGasSource, newCapacity);
      ResizeLinear(ref HasExternalSupply, oldCapacity, newCapacity);
      ResizeContiguousGases(ref ExternalLungSupply, oldCapacity, newCapacity);
      ResizeContiguousGases(ref ExternalNetConsumed, oldCapacity, newCapacity);
      Array.Resize(ref Metabolizers, newCapacity);
      Capacity = newCapacity;
    }

    private void ResizeLinear<T>(ref NativeArray<T> array, int oldSize, int newSize) where T : struct
    {
      var newArray = new NativeArray<T>(newSize, Allocator.Persistent);
      if (array.IsCreated) { NativeArray<T>.Copy(array, 0, newArray, 0, Count); array.Dispose(); }
      array = newArray;
    }

    private void ResizeContiguousGases<T>(ref NativeArray<T> array, int oldCap, int newCap) where T : struct
    {
      var newArray = new NativeArray<T>(newCap * GasCount, Allocator.Persistent);
      if (array.IsCreated) { NativeArray<T>.Copy(array, 0, newArray, 0, Count * GasCount); array.Dispose(); }
      array = newArray;
    }

    private void ResizeContiguousGlobins(ref NativeArray<GlobinState> array, int oldCap, int newCap)
    {
      var newArray = new NativeArray<GlobinState>(newCap * GlobinCount, Allocator.Persistent);
      if (array.IsCreated) { NativeArray<GlobinState>.Copy(array, 0, newArray, 0, Count * GlobinCount); array.Dispose(); }
      for (int i = Count * GlobinCount; i < newCap * GlobinCount; i++) newArray[i] = new GlobinState { OccupantGasId = -1 };
      array = newArray;
    }

    private void ResizeContiguousSubs(ref NativeArray<bool> array, int oldCap, int newCap)
    {
      var newArray = new NativeArray<bool>(newCap * SubscriptionCount, Allocator.Persistent);
      if (array.IsCreated) { NativeArray<bool>.Copy(array, 0, newArray, 0, Count * SubscriptionCount); array.Dispose(); }
      array = newArray;
    }

    public void Dispose()
    {
      if (GlobinSpecs.IsCreated) GlobinSpecs.Dispose();
      TotalFreePlasma.SafeDispose(); AllostericSums.SafeDispose(); BohrShifts.SafeDispose();
      GlobinMaxCollisionDiameters.SafeDispose(); GlobinMinElectronegativities.SafeDispose();
      GlobinMetalHardnesses.SafeDispose(); GlobinCapacities.SafeDispose(); InvGlobinCapacities.SafeDispose();
      GasToGlobinIndex.SafeDispose(); GasStructuralWarpingPotentials.SafeDispose(); GasRadioactivities.SafeDispose();
      GlobinGasIds.SafeDispose(); GlobinGasGlobinIds.SafeDispose(); GlobinGasBindAffinities.SafeDispose();
      GlobinHillCoeffs.SafeDispose(); GlobinP50Fracs.SafeDispose(); GlobinGasLungUptakeFactors.SafeDispose();
      GlobinGasLungReleaseFactors.SafeDispose(); FreeGasIds.SafeDispose(); FreeGasUptakeFactors.SafeDispose();
      FreeGasReleaseFactors.SafeDispose(); Reactions.SafeDispose(); Solubilities.SafeDispose();
      GasAffinities.SafeDispose(); GasGlobinAffinities.SafeDispose(); ExcretedEfficiencies.SafeDispose();
      LungStorage.SafeDispose(); PlasmaStorage.SafeDispose(); GlobinLevels.SafeDispose();
      EntityLungNetFlux.SafeDispose(); WorldIndices.SafeDispose(); EntitySolubilityScale.SafeDispose();
      EntityLungEfficiency.SafeDispose(); EntityPlasmaExchangeEfficiency.SafeDispose(); EntityPlasmaFiltrationEfficiency.SafeDispose();
      SubGasIds.SafeDispose(); SubThresholds.SafeDispose(); SubHysteresis.SafeDispose(); SubIsExcess.SafeDispose();
      SubIsPlasma.SafeDispose(); StageActive.SafeDispose(); StageJustTriggered.SafeDispose(); StageJustReset.SafeDispose();
      GasDangerThresholds.SafeDispose(); HasExternalSupply.SafeDispose(); ExternalLungSupply.SafeDispose(); ExternalNetConsumed.SafeDispose();
    }

    public int GetGasIndex(int entityIdx, int gasIdx)
    {
      int blockIdx = entityIdx / BLOCK_SIZE; int laneIdx = entityIdx % BLOCK_SIZE;
      return (blockIdx * GasCount * BLOCK_SIZE) + (gasIdx * BLOCK_SIZE) + laneIdx;
    }

    public int GetGlobinIndex(int entityIdx, int globinIdx)
    {
      int blockIdx = entityIdx / BLOCK_SIZE; int laneIdx = entityIdx % BLOCK_SIZE;
      return (blockIdx * GlobinCount * BLOCK_SIZE) + (globinIdx * BLOCK_SIZE) + laneIdx;
    }
  }
}
