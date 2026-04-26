using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;

using SolarWeb.Pneuma.Atoms;
using SolarWeb.Pneuma.Gas;
using SolarWeb.Pneuma.GasExchange;

namespace SolarWeb.Pneuma.Metabolism
{
  public class MetabolismState : IDisposable
  {
    // Maps MetabolismID (Species/Type) to its specific data batch
    public Dictionary<int, IMetabolismBatch> Batches;
    public ExchangerBuffer ExchangerBuffer;
    private GasRegistry gasRegistry;
    private AtomicRegistry atomicRegistry;

    public int InitialBatchCapacity { get; private set; }
    public int PawnBatchCount { get; private set; } = 0;

    public MetabolismState(ExchangerBuffer buffer, GasRegistry gasRegistry, AtomicRegistry atomicRegistry, int initialBatchCapacity = 128)
    {
      ExchangerBuffer = buffer;
      InitialBatchCapacity = initialBatchCapacity;
      Batches = new Dictionary<int, IMetabolismBatch>();
      this.gasRegistry = gasRegistry;
      this.atomicRegistry = atomicRegistry;
    }

    // --- Danger Level Buffers ---
    public NativeArray<MetabolismDangerCriteria> BatchDangerSpecs;
    public NativeArray<GasDangerThreshold> BatchGasDangerMatrix; // [batchIdx * GasCount + gasId]
    public NativeArray<ulong> BatchRelevantGasMasks; // [batchIdx]
    public NativeArray<ulong> BatchRequiredGasMasks; // [batchIdx]
    public NativeArray<float> DangerLevels; // [batchIdx * RegionStride + regIdx]

    // --- Cell Danger Level Buffers ---
    public NativeArray<float> CellDangerLevels; // [batchIdx * Stride + simIdx]
    public NativeArray<float> CellPhysicalHazardLevels; // [hazardIdx * Stride + simIdx]

    // --- Physical Hazard Buffers (Per-Region) ---
    public const int PhysicalHazardCount = 4;
    public const int HazardIdx_Explosion = 0;
    public const int HazardIdx_Radiation = 1;
    public const int HazardIdx_Caustic = 2;
    public const int HazardIdx_Thermal = 3;
    public NativeArray<float> PhysicalHazardLevels; // [hazardIdx * RegionStride + regIdx]

    private int currentRegionStride = 0;
    private int currentCellStride = 0;

    public void InitializeDangerBuffers(int regionStride, int cellStride)
    {
      // Safety: Dispose existing buffers if this is a re-initialization (e.g. after map load/rebuild)
      if (BatchDangerSpecs.IsCreated) BatchDangerSpecs.Dispose();
      if (BatchGasDangerMatrix.IsCreated) BatchGasDangerMatrix.Dispose();
      if (BatchRelevantGasMasks.IsCreated) BatchRelevantGasMasks.Dispose();
      if (BatchRequiredGasMasks.IsCreated) BatchRequiredGasMasks.Dispose();
      if (DangerLevels.IsCreated) DangerLevels.Dispose();
      if (PhysicalHazardLevels.IsCreated) PhysicalHazardLevels.Dispose();
      if (CellDangerLevels.IsCreated) CellDangerLevels.Dispose();
      if (CellPhysicalHazardLevels.IsCreated) CellPhysicalHazardLevels.Dispose();

      currentRegionStride = regionStride;
      currentCellStride = cellStride;

      const int InitialBatchCount = 16;
      BatchDangerSpecs = new NativeArray<MetabolismDangerCriteria>(InitialBatchCount, Allocator.Persistent);
      BatchGasDangerMatrix = new NativeArray<GasDangerThreshold>(InitialBatchCount * gasRegistry.GasCount, Allocator.Persistent);
      BatchRelevantGasMasks = new NativeArray<ulong>(InitialBatchCount, Allocator.Persistent);
      BatchRequiredGasMasks = new NativeArray<ulong>(InitialBatchCount, Allocator.Persistent);
      DangerLevels = new NativeArray<float>(InitialBatchCount * regionStride, Allocator.Persistent);
      PhysicalHazardLevels = new NativeArray<float>(PhysicalHazardCount * regionStride, Allocator.Persistent);

      CellDangerLevels = new NativeArray<float>(InitialBatchCount * cellStride, Allocator.Persistent);
      CellPhysicalHazardLevels = new NativeArray<float>(PhysicalHazardCount * cellStride, Allocator.Persistent);

      // Explicitly zero out all buffers to prevent garbage data from causing false danger reports
      unsafe
      {
        Unity.Collections.LowLevel.Unsafe.UnsafeUtility.MemClear(Unity.Collections.LowLevel.Unsafe.NativeArrayUnsafeUtility.GetUnsafePtr(BatchDangerSpecs), BatchDangerSpecs.Length * Unity.Collections.LowLevel.Unsafe.UnsafeUtility.SizeOf<MetabolismDangerCriteria>());
        Unity.Collections.LowLevel.Unsafe.UnsafeUtility.MemClear(Unity.Collections.LowLevel.Unsafe.NativeArrayUnsafeUtility.GetUnsafePtr(BatchGasDangerMatrix), BatchGasDangerMatrix.Length * sizeof(float) * 4); // GasDangerThreshold is 4 floats
        Unity.Collections.LowLevel.Unsafe.UnsafeUtility.MemClear(Unity.Collections.LowLevel.Unsafe.NativeArrayUnsafeUtility.GetUnsafePtr(BatchRelevantGasMasks), BatchRelevantGasMasks.Length * sizeof(ulong));
        Unity.Collections.LowLevel.Unsafe.UnsafeUtility.MemClear(Unity.Collections.LowLevel.Unsafe.NativeArrayUnsafeUtility.GetUnsafePtr(BatchRequiredGasMasks), BatchRequiredGasMasks.Length * sizeof(ulong));
        Unity.Collections.LowLevel.Unsafe.UnsafeUtility.MemClear(Unity.Collections.LowLevel.Unsafe.NativeArrayUnsafeUtility.GetUnsafePtr(DangerLevels), DangerLevels.Length * sizeof(float));
        Unity.Collections.LowLevel.Unsafe.UnsafeUtility.MemClear(Unity.Collections.LowLevel.Unsafe.NativeArrayUnsafeUtility.GetUnsafePtr(PhysicalHazardLevels), PhysicalHazardLevels.Length * sizeof(float));
        Unity.Collections.LowLevel.Unsafe.UnsafeUtility.MemClear(Unity.Collections.LowLevel.Unsafe.NativeArrayUnsafeUtility.GetUnsafePtr(CellDangerLevels), CellDangerLevels.Length * sizeof(float));
        Unity.Collections.LowLevel.Unsafe.UnsafeUtility.MemClear(Unity.Collections.LowLevel.Unsafe.NativeArrayUnsafeUtility.GetUnsafePtr(CellPhysicalHazardLevels), CellPhysicalHazardLevels.Length * sizeof(float));
      }

      // Reset internal counter and restore criteria for all existing batches after buffer re-initialization
      PawnBatchCount = 0;
      SyncAllBatchesToNative();
    }

    /// <summary>
    /// Synchronizes the biological criteria and gas danger thresholds for all active batches 
    /// into the native arrays used by Burst jobs. This must be called whenever the danger 
    /// buffers are re-initialized (e.g., during grid rebuilds).
    /// </summary>
    public void SyncAllBatchesToNative()
    {
      if (!BatchDangerSpecs.IsCreated) return;

      foreach (var batch in Batches.Values)
      {
        if (batch is MetabolismBatch pBatch)
        {
          int pIdx = PawnBatchCount++;
          pBatch.DangerBufferIndex = pIdx;

          if (pIdx < 0 || pIdx >= BatchDangerSpecs.Length) continue;

          BatchDangerSpecs[pIdx] = pBatch.DangerCriteria;
          BatchRelevantGasMasks[pIdx] = pBatch.RelevantGasMask;
          BatchRequiredGasMasks[pIdx] = pBatch.RequiredGasMask;

          int matrixBase = pIdx * gasRegistry.GasCount;
          for (int g = 0; g < gasRegistry.GasCount; g++)
          {
            BatchGasDangerMatrix[matrixBase + g] = pBatch.GasDangerThresholds[g];
          }
        }
      }
    }

    public int CurrentRegionStride => currentRegionStride;
    public int CurrentCellStride => currentCellStride;

    public void ClearCellDangerBuffers()
    {
      if (CellDangerLevels.IsCreated)
      {
        unsafe
        {
          Unity.Collections.LowLevel.Unsafe.UnsafeUtility.MemClear(
            Unity.Collections.LowLevel.Unsafe.NativeArrayUnsafeUtility.GetUnsafePtr(CellDangerLevels),
            CellDangerLevels.Length * sizeof(float));
        }
      }
      if (CellPhysicalHazardLevels.IsCreated)
      {
        unsafe
        {
          Unity.Collections.LowLevel.Unsafe.UnsafeUtility.MemClear(
            Unity.Collections.LowLevel.Unsafe.NativeArrayUnsafeUtility.GetUnsafePtr(CellPhysicalHazardLevels),
            CellPhysicalHazardLevels.Length * sizeof(float));
        }
      }
    }

    public float GetPhysicalHazard(int hazardIdx, int regionIdx)
    {
      if (!PhysicalHazardLevels.IsCreated || regionIdx < 0 || regionIdx >= currentRegionStride) return 0;
      return PhysicalHazardLevels[hazardIdx * currentRegionStride + regionIdx];
    }

    public float GetPhysicalHazardAt(int hazardIdx, int regionIdx, int simIdx = -1)
    {
      if (simIdx >= 0 && simIdx < currentCellStride && CellPhysicalHazardLevels.IsCreated)
      {
        float val = CellPhysicalHazardLevels[hazardIdx * currentCellStride + simIdx];
        if (val > 0) return val;
      }
      return GetPhysicalHazard(hazardIdx, regionIdx);
    }

    public float GetExplosionRisk(int regionIdx, int simIdx = -1) => GetPhysicalHazardAt(HazardIdx_Explosion, regionIdx, simIdx);
    public float GetRadiationHazard(int regionIdx, int simIdx = -1) => GetPhysicalHazardAt(HazardIdx_Radiation, regionIdx, simIdx);
    public float GetCausticHazard(int regionIdx, int simIdx = -1) => GetPhysicalHazardAt(HazardIdx_Caustic, regionIdx, simIdx);
    public float GetThermalHazard(int regionIdx, int simIdx = -1) => GetPhysicalHazardAt(HazardIdx_Thermal, regionIdx, simIdx);

    public float GetCellPhysicalHazard(int hazardIdx, int simIdx)
    {
      if (!CellPhysicalHazardLevels.IsCreated || simIdx < 0 || simIdx >= currentCellStride) return 0;
      return CellPhysicalHazardLevels[hazardIdx * currentCellStride + simIdx];
    }

    /// <summary>
    /// Retrieves an existing batch or creates a new one based on a template.
    /// </summary>
    public IMetabolismBatch GetOrCreateBatch(GasMetabolism template)
    {
      if (!Batches.TryGetValue(template.Id, out var batch))
      {
        if (template.IsSimplified)
        {
          batch = new SimplifiedMetabolismBatch(this, Batches.Count, template, InitialBatchCapacity, gasRegistry, atomicRegistry, ExchangerBuffer);
        }
        else
        {
          int pIdx = PawnBatchCount++;
          var pBatch = new MetabolismBatch(this, pIdx, ExchangerBuffer, template, InitialBatchCapacity, gasRegistry, atomicRegistry);
          batch = pBatch;

          // Sync to DangerSpecs for Burst job (Pawn batches only)
          if (pIdx >= BatchDangerSpecs.Length)
          {
            int oldCap = BatchDangerSpecs.Length;
            int newCap = oldCap * 2;
            ResizeBuffer(ref BatchDangerSpecs, newCap);
            ResizeBuffer(ref BatchGasDangerMatrix, newCap * gasRegistry.GasCount);
            ResizeBuffer(ref BatchRelevantGasMasks, newCap);
            ResizeBuffer(ref BatchRequiredGasMasks, newCap);
            ResizeBuffer(ref DangerLevels, newCap * currentRegionStride);
            ResizeBuffer(ref CellDangerLevels, newCap * currentCellStride);
          }

          BatchDangerSpecs[pIdx] = pBatch.DangerCriteria;
          BatchRelevantGasMasks[pIdx] = pBatch.RelevantGasMask;
          BatchRequiredGasMasks[pIdx] = pBatch.RequiredGasMask;

          // Populate gas danger matrix row
          int matrixBase = pIdx * gasRegistry.GasCount;
          for (int g = 0; g < gasRegistry.GasCount; g++)
          {
            BatchGasDangerMatrix[matrixBase + g] = pBatch.GasDangerThresholds[g];
          }
        }

        Batches.Add(template.Id, batch);
      }
      return batch;
    }

    private void ResizeBuffer<T>(ref NativeArray<T> array, int newSize) where T : struct
    {
      var newArray = new NativeArray<T>(newSize, Allocator.Persistent);
      if (array.IsCreated)
      {
        NativeArray<T>.Copy(array, newArray, math.min(array.Length, newSize));

        // Zero out the new portion
        if (newSize > array.Length)
        {
          unsafe
          {
            byte* ptr = (byte*)Unity.Collections.LowLevel.Unsafe.NativeArrayUnsafeUtility.GetUnsafePtr(newArray);
            int offset = array.Length * Unity.Collections.LowLevel.Unsafe.UnsafeUtility.SizeOf<T>();
            int size = (newSize - array.Length) * Unity.Collections.LowLevel.Unsafe.UnsafeUtility.SizeOf<T>();
            Unity.Collections.LowLevel.Unsafe.UnsafeUtility.MemClear(ptr + offset, size);
          }
        }

        array.Dispose();
      }
      array = newArray;
    }

    public void AddMetabolizer(IMetabolizer metabolizer, GasMetabolism template)
    {
      var batch = GetOrCreateBatch(template);

      int indexInBatch = batch.AddEntity(metabolizer);

      metabolizer.BatchIndex = indexInBatch;
      metabolizer.BatchReference = batch;
    }

    public void RemoveMetabolizer(IMetabolizer metabolizer, GasMetabolism template)
    {
      if (Batches.TryGetValue(template.Id, out var batch))
      {
        batch.RemoveEntity(metabolizer.BatchIndex);

        metabolizer.BatchIndex = -1;
        metabolizer.BatchReference = null;
      }
    }


    public void Dispose()
    {
      foreach (var batch in Batches.Values)
      {
        batch.Dispose();
      }
      Batches.Clear();
      if (BatchDangerSpecs.IsCreated) BatchDangerSpecs.Dispose();
      if (BatchGasDangerMatrix.IsCreated) BatchGasDangerMatrix.Dispose();
      if (BatchRelevantGasMasks.IsCreated) BatchRelevantGasMasks.Dispose();
      if (BatchRequiredGasMasks.IsCreated) BatchRequiredGasMasks.Dispose();
      if (DangerLevels.IsCreated) DangerLevels.Dispose();
      if (PhysicalHazardLevels.IsCreated) PhysicalHazardLevels.Dispose();
      if (CellDangerLevels.IsCreated) CellDangerLevels.Dispose();
      if (CellPhysicalHazardLevels.IsCreated) CellPhysicalHazardLevels.Dispose();
    }
  }
}
