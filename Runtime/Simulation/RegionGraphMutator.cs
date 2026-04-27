using System;
using SolarWeb.Pneuma.Data;
using SolarWeb.Pneuma.Grid;
using UnityEngine;

namespace SolarWeb.Pneuma.Simulation
{
  /// <summary>
  /// Main-thread utility for dynamic region split and merge operations.
  /// Must be called on the main thread with all jobs complete.
  /// </summary>
  public class RegionGraphMutator
  {
    private const int HysteresisTicks = 10;

    private AtmosphereGrid State => manager.State;
    private readonly AtmosphereManager manager;

    public RegionGraphMutator(AtmosphereManager manager)
    {
      this.manager = manager;
    }

    // ── Public API ──────────────────────────────────────────────────────────

    /// <summary>
    /// Queue a world cell for splitting into a dynamic mini-region.
    /// Safe to call during topology events (breach handling, TopologyMapper).
    /// The split is deferred until ProcessQueue() is called.
    /// </summary>
    public void EnqueueSplit(int worldIdx)
    {
      if (worldIdx < 0 || worldIdx >= State.WorldCellCount)
        return;

      int existingReg = State.GridLookups.WorldToRegionIndex[worldIdx];

      // Already dynamic — skip
      if (existingReg >= State.DynamicRegionPoolStart && existingReg < State.SentinelRegionIndex)
        return;

      // No valid static region — skip
      if (existingReg < 0 || existingReg >= State.SimRegionCount)
        return;

      // Pool cap guard
      if (State.DynRegionCount >= AtmosphereGrid.MaxDynRegions - 1)
      {
        Debug.LogWarning($"[RegionGraphMutator] Dynamic region pool exhausted — skipping split for worldIdx {worldIdx}");
        return;
      }

      State.DynamicRegions.PendingSplitWorldIndices.Enqueue(worldIdx);
    }

    /// <summary>
    /// Drain PendingSplitWorldIndices and materialise each split.
    /// Call once per tick on the main thread, BEFORE scheduling diffusion jobs.
    /// After this returns, DynFaceGasFlux and DynFaceThermalFlux are sized correctly.
    /// </summary>
    public void ProcessQueue()
    {
      while (State.DynamicRegions.PendingSplitWorldIndices.TryDequeue(out int worldIdx))
        ProcessSplit(worldIdx);

      while (State.Events.PendingInjections.TryDequeue(out var req))
        ProcessInjection(req);
    }

    /// <summary>
    /// Drain DynMergeQueue, merging each equilibrated dynamic region back into its parent.
    /// Call once per tick on the main thread, AFTER all dynamic face flux jobs complete.
    /// </summary>
    public void AbsorbEquilibrated()
    {
      while (State.DynamicRegions.MergeQueue.TryDequeue(out int dynRegIdx))
        AbsorbRegion(dynRegIdx);
    }

    // ── Split ───────────────────────────────────────────────────────────────

    private void ProcessSplit(int worldIdx)
    {
      int parentRegIdx = State.GridLookups.WorldToRegionIndex[worldIdx];

      // Re-validate: must still point to a static region (state may have changed since enqueue)
      if (parentRegIdx < 0 || parentRegIdx >= State.SimRegionCount)
        return;

      // Find a free dynamic pool slot (scan for DynRegionParent[slot] == -1)
      int slot = FindFreeSlot();
      if (slot == -1)
      {
        Debug.LogWarning("[RegionGraphMutator] No free dynamic region slot.");
        return;
      }

      int dynRegIdx = State.DynamicRegionPoolStart + slot;

      // ── Extract proportional gas from parent ──────────────────────────
      float cellVol = State.WorldPhysicsBuffer.CellVolumes[worldIdx];
      float parentVol = State.RegionPhysicsBuffer.RegionVolumes[parentRegIdx];
      float frac = (parentVol > 1e-6f) ? Math.Min(cellVol / parentVol, 1f) : 0f;

      for (int g = 0; g < State.GasCount; g++)
      {
        int parentIdx = State.GetRegionUMoleIndex(g, parentRegIdx);
        int dynIdx = State.GetRegionUMoleIndex(g, dynRegIdx);

        long parentAmt = State.RegionGasComposition.uMoles[parentIdx];
        long extracted = (long)(parentAmt * frac);

        State.RegionGasComposition.uMoles[dynIdx] = extracted;
        State.RegionGasComposition.uMoles[parentIdx] = parentAmt - extracted;
      }

      // Keep TotalUMoles consistent (will be recomputed by SyncRegionTotalMoles next tick)
      long parentTotal = State.RegionGasComposition.TotalUMoles[parentRegIdx];
      long dynTotal = (long)(parentTotal * frac);
      State.RegionGasComposition.TotalUMoles[dynRegIdx] = dynTotal;
      State.RegionGasComposition.TotalUMoles[parentRegIdx] = parentTotal - dynTotal;

      // ── Copy temperature and volume ───────────────────────────────────
      float parentTempK = State.RegionPhysicsBuffer.TemperatureK[parentRegIdx];
      State.RegionPhysicsBuffer.MaxPressureKpa[dynRegIdx] = State.RegionPhysicsBuffer.MaxPressureKpa[parentRegIdx];
      State.RegionPhysicsBuffer.TemperatureK[dynRegIdx] = parentTempK;
      State.RegionPhysicsBuffer.PreviousTemperatureK[dynRegIdx] = parentTempK;
      State.RegionPhysicsBuffer.RegionVolumes[dynRegIdx] = cellVol;
      State.RegionPhysicsBuffer.RegionVolumes[parentRegIdx] -= cellVol;

      float parentECap = State.RegionPhysicsBuffer.EnclosingThermalCapacity[parentRegIdx];
      float dynECap = parentECap * frac;
      State.RegionPhysicsBuffer.EnclosingThermalCapacity[dynRegIdx] = dynECap;
      State.RegionPhysicsBuffer.EnclosingThermalCapacity[parentRegIdx] = parentECap - dynECap;

      float parentICap = State.RegionPhysicsBuffer.InternalMassThermalCapacity[parentRegIdx];
      float dynICap = parentICap * frac;
      State.RegionPhysicsBuffer.InternalMassThermalCapacity[dynRegIdx] = dynICap;
      State.RegionPhysicsBuffer.InternalMassThermalCapacity[parentRegIdx] = parentICap - dynICap;

      float parentETemp = State.RegionPhysicsBuffer.EnclosingTemperatureK[parentRegIdx];
      State.RegionPhysicsBuffer.EnclosingTemperatureK[dynRegIdx] = parentETemp;
      State.RegionPhysicsBuffer.PreviousEnclosingTemperatureK[dynRegIdx] = parentETemp;

      float parentITemp = State.RegionPhysicsBuffer.InternalMassTemperatureK[parentRegIdx];
      State.RegionPhysicsBuffer.InternalMassTemperatureK[dynRegIdx] = parentITemp;
      State.RegionPhysicsBuffer.PreviousInternalMassTemperatureK[dynRegIdx] = parentITemp;

      float parentECond = State.RegionPhysicsBuffer.EnclosingThermalConductance[parentRegIdx];
      float dynECond = parentECond * frac;
      State.RegionPhysicsBuffer.EnclosingThermalConductance[dynRegIdx] = dynECond;
      State.RegionPhysicsBuffer.EnclosingThermalConductance[parentRegIdx] = parentECond - dynECond;

      float parentICond = State.RegionPhysicsBuffer.InternalMassThermalConductance[parentRegIdx];
      float dynICond = parentICond * frac;
      State.RegionPhysicsBuffer.InternalMassThermalConductance[dynRegIdx] = dynICond;
      State.RegionPhysicsBuffer.InternalMassThermalConductance[parentRegIdx] = parentICond - dynICond;

      // ── Activate dynamic slot ─────────────────────────────────────────
      State.RegionStates.IsActive[dynRegIdx] = true;
      State.RegionStates.ActiveTicks[dynRegIdx] = HysteresisTicks;
      State.RegionStates.EquilibriumTicks[dynRegIdx] = 0;
      State.RegionStates.RegionToRoomIndex[dynRegIdx] = State.RegionStates.RegionToRoomIndex[parentRegIdx];
      State.RegionStates.RegionToRoomID[dynRegIdx] = State.RegionStates.RegionToRoomID[parentRegIdx];
      State.RegionGasComposition.PressureKpa[dynRegIdx] = State.RegionGasComposition.PressureKpa[parentRegIdx];

      // ── Update lookups ────────────────────────────────────────────────
      State.GridLookups.WorldToRegionIndex[worldIdx] = dynRegIdx;
      State.DynamicRegions.Parent[slot] = parentRegIdx;
      State.DynamicRegions.WorldIdx[slot] = worldIdx;

      // ── Build dynamic faces from static cell topology cache ───────────
      BuildFacesForDynamicRegion(slot, dynRegIdx, parentRegIdx, worldIdx, false);

      // ── Resize per-tick flux output lists to match new total face count ─
      int totalFaces = State.DynamicFaces.RegionA.Length;
      ResizeDynFluxLists(totalFaces);

      State.DynRegionCount++;
    }

    private void ProcessInjection(InjectionRequest req)
    {
      int worldIdx = req.WorldIdx;
      int parentRegIdx = State.GridLookups.WorldToRegionIndex[worldIdx];

      // Already dynamic or invalid — skip
      if (parentRegIdx < 0 || parentRegIdx >= State.SimRegionCount)
        return;

      int slot = FindFreeSlot();
      if (slot == -1) return;

      int dynRegIdx = State.DynamicRegionPoolStart + slot;

      // ── Extract proportional gas from parent ──────────────────────────
      float cellVol = State.WorldPhysicsBuffer.CellVolumes[worldIdx];
      float parentVol = State.RegionPhysicsBuffer.RegionVolumes[parentRegIdx];
      float frac = (parentVol > 1e-6f) ? Math.Min(cellVol / parentVol, 1f) : 0f;

      for (int g = 0; g < State.GasCount; g++)
      {
        int parentIdx = State.GetRegionUMoleIndex(g, parentRegIdx);
        int dynIdx = State.GetRegionUMoleIndex(g, dynRegIdx);

        long parentAmt = State.RegionGasComposition.uMoles[parentIdx];
        long extracted = (long)(parentAmt * frac);

        State.RegionGasComposition.uMoles[dynIdx] = extracted + ((g == req.GasId) ? req.AmountUMol : 0L);
        State.RegionGasComposition.uMoles[parentIdx] = parentAmt - extracted;
      }

      long parentTotal = State.RegionGasComposition.TotalUMoles[parentRegIdx];
      long dynTotal = (long)(parentTotal * frac);
      State.RegionGasComposition.TotalUMoles[dynRegIdx] = dynTotal + req.AmountUMol;
      State.RegionGasComposition.TotalUMoles[parentRegIdx] = parentTotal - dynTotal;

      // ── Mix temperatures and split volume/capacity ────────────────────
      float tParent = State.RegionPhysicsBuffer.TemperatureK[parentRegIdx];
      State.RegionPhysicsBuffer.MaxPressureKpa[dynRegIdx] = State.RegionPhysicsBuffer.MaxPressureKpa[parentRegIdx];
      if (dynTotal + req.AmountUMol > 0)
      {
        State.RegionPhysicsBuffer.TemperatureK[dynRegIdx] =
          ((tParent * dynTotal) + (req.TemperatureK * req.AmountUMol)) / (dynTotal + req.AmountUMol);
      }
      else
      {
        State.RegionPhysicsBuffer.TemperatureK[dynRegIdx] = req.TemperatureK;
      }

      State.RegionPhysicsBuffer.RegionVolumes[dynRegIdx] = cellVol;
      State.RegionPhysicsBuffer.RegionVolumes[parentRegIdx] -= cellVol;

      float parentECap = State.RegionPhysicsBuffer.EnclosingThermalCapacity[parentRegIdx];
      float dynECap = parentECap * frac;
      State.RegionPhysicsBuffer.EnclosingThermalCapacity[dynRegIdx] = dynECap;
      State.RegionPhysicsBuffer.EnclosingThermalCapacity[parentRegIdx] = parentECap - dynECap;

      float parentICap = State.RegionPhysicsBuffer.InternalMassThermalCapacity[parentRegIdx];
      float dynICap = parentICap * frac;
      State.RegionPhysicsBuffer.InternalMassThermalCapacity[dynRegIdx] = dynICap;
      State.RegionPhysicsBuffer.InternalMassThermalCapacity[parentRegIdx] = parentICap - dynICap;

      float parentETemp = State.RegionPhysicsBuffer.EnclosingTemperatureK[parentRegIdx];
      State.RegionPhysicsBuffer.EnclosingTemperatureK[dynRegIdx] = parentETemp;
      State.RegionPhysicsBuffer.PreviousEnclosingTemperatureK[dynRegIdx] = parentETemp;

      float parentITemp = State.RegionPhysicsBuffer.InternalMassTemperatureK[parentRegIdx];
      State.RegionPhysicsBuffer.InternalMassTemperatureK[dynRegIdx] = parentITemp;
      State.RegionPhysicsBuffer.PreviousInternalMassTemperatureK[dynRegIdx] = parentITemp;

      float parentECond = State.RegionPhysicsBuffer.EnclosingThermalConductance[parentRegIdx];
      float dynECond = parentECond * frac;
      State.RegionPhysicsBuffer.EnclosingThermalConductance[dynRegIdx] = dynECond;
      State.RegionPhysicsBuffer.EnclosingThermalConductance[parentRegIdx] = parentECond - dynECond;

      float parentICond = State.RegionPhysicsBuffer.InternalMassThermalConductance[parentRegIdx];
      float dynICond = parentICond * frac;
      State.RegionPhysicsBuffer.InternalMassThermalConductance[dynRegIdx] = dynICond;
      State.RegionPhysicsBuffer.InternalMassThermalConductance[parentRegIdx] = parentICond - dynICond;

      // ── Activate dynamic slot ─────────────────────────────────────────
      State.RegionStates.IsActive[dynRegIdx] = true;
      State.RegionStates.ActiveTicks[dynRegIdx] = HysteresisTicks;
      State.RegionStates.EquilibriumTicks[dynRegIdx] = 0;
      State.RegionStates.RegionToRoomIndex[dynRegIdx] = State.RegionStates.RegionToRoomIndex[parentRegIdx];
      State.RegionStates.RegionToRoomID[dynRegIdx] = State.RegionStates.RegionToRoomID[parentRegIdx];
      State.RegionGasComposition.PressureKpa[dynRegIdx] = State.RegionGasComposition.PressureKpa[parentRegIdx];

      State.GridLookups.WorldToRegionIndex[worldIdx] = dynRegIdx;
      State.DynamicRegions.Parent[slot] = parentRegIdx;
      State.DynamicRegions.WorldIdx[slot] = worldIdx;

      // ── Build bidirectional dynamic faces ─────────────────────────────
      BuildFacesForDynamicRegion(slot, dynRegIdx, parentRegIdx, worldIdx, false);

      int totalFaces = State.DynamicFaces.RegionA.Length;
      ResizeDynFluxLists(totalFaces);
      State.DynRegionCount++;
    }

    private void BuildFacesForDynamicRegion(int slot, int dynRegIdx, int parentRegIdx, int worldIdx, bool isOneWay)
    {
      int simIdx = State.GridLookups.WorldToSimIndex[worldIdx];
      int faceStart = State.DynamicFaces.RegionA.Length;
      int facesAdded = 0;

      if (simIdx >= 0 && simIdx < State.SimCellCount)
      {
        int cellOffset = State.GridLookups.CellFaceOffsets[simIdx];
        int cellCount = State.GridLookups.CellFaceCounts[simIdx];

        for (int k = 0; k < cellCount; k++)
        {
          int faceIdx = State.GridLookups.CellFaceIndices[cellOffset + k];

          int cellA = State.GridLookups.CellFaceCellA[faceIdx];
          int cellB = State.GridLookups.CellFaceCellB[faceIdx];

          int neighborSimIdx = (cellA == simIdx) ? cellB : cellA;

          int neighborRegIdx;
          if (neighborSimIdx == State.SentinelCellIndex)
          {
            neighborRegIdx = State.SentinelRegionIndex;
          }
          else
          {
            int neighborWorldIdx = State.GridLookups.SimToWorldIndex[neighborSimIdx];
            neighborRegIdx = State.GridLookups.WorldToRegionIndex[neighborWorldIdx];
          }

          float permeability = State.GridLookups.CellFacePermeability[faceIdx];
          byte faceType = State.GridLookups.CellFaceType[faceIdx];
          float conductivity = State.GridLookups.CellFaceConductivity[faceIdx];
          float surfaceArea = State.GridLookups.CellFaceSurfaceArea[faceIdx];
          float minColDia = State.GridLookups.CellFaceMinCollisionDiameter[faceIdx];
          float maxColDia = State.GridLookups.CellFaceMaxCollisionDiameter[faceIdx];

          if (permeability <= 0f && conductivity <= 0f)
            continue;

          if (neighborRegIdx >= State.DynamicRegionPoolStart && neighborRegIdx < State.SentinelRegionIndex)
          {
            int neighborSlot = neighborRegIdx - State.DynamicRegionPoolStart;
            if (State.DynamicRegions.Parent[neighborSlot] == parentRegIdx)
            {
              int nFaceStart = State.DynamicRegions.FaceStart[neighborSlot];
              int nFaceCount = State.DynamicRegions.FaceCount[neighborSlot];
              for (int n = 0; n < nFaceCount; n++)
              {
                int ni = nFaceStart + n;
                if (State.DynamicFaces.SourceEdge[ni] == faceIdx)
                {
                  State.DynamicFaces.RegionA[ni] = State.SentinelRegionIndex;
                  State.DynamicFaces.RegionB[ni] = State.SentinelRegionIndex;
                  State.DynamicFaces.SourceEdge[ni] = -1;
                  break;
                }
              }
            }
          }

          State.DynamicFaces.RegionA.Add(dynRegIdx);
          State.DynamicFaces.RegionB.Add(neighborRegIdx);
          State.DynamicFaces.Type.Add(faceType);
          State.DynamicFaces.GasPermeability.Add(permeability);
          State.DynamicFaces.ThermalConductivity.Add(conductivity);
          State.DynamicFaces.SurfaceArea.Add(surfaceArea);
          State.DynamicFaces.MinCollisionDiameter.Add(minColDia);
          State.DynamicFaces.MaxCollisionDiameter.Add(maxColDia);
          State.DynamicFaces.SourceEdge.Add(faceIdx);

          State.DynamicFaces.LimitedVel.Add(0f);
          State.DynamicFaces.MaxSafeAdvection.Add(0f);
          State.DynamicFaces.TFactor.Add(0f);
          State.DynamicFaces.FlowDirection.Add(isOneWay ? (sbyte)1 : (sbyte)0);
          State.DynamicFaces.ActivePumpRate.Add(0f);
          State.DynamicFaces.MaxPumpPressureKpa.Add(0f);
          State.DynamicFaces.RegulatorKpa.Add(-1f);
          State.DynamicFaces.WindExposureX.Add(0f);
          State.DynamicFaces.WindExposureZ.Add(0f);
          State.DynamicFaces.FacePressureOffsetKpa.Add(0f);

          facesAdded++;
        }
      }

      State.DynamicRegions.FaceStart[slot] = faceStart;
      State.DynamicRegions.FaceCount[slot] = facesAdded;

      manager.WindMap?.MarkDirty();
    }

    // ── Merge ───────────────────────────────────────────────────────────────

    private void AbsorbRegion(int dynRegIdx)
    {
      int slot = dynRegIdx - State.DynamicRegionPoolStart;
      if (slot < 0 || slot >= AtmosphereGrid.MaxDynRegions)
        return;

      int parentRegIdx = State.DynamicRegions.Parent[slot];
      if (parentRegIdx < 0)
        return; // already merged or slot was never allocated

      int worldIdx = State.DynamicRegions.WorldIdx[slot];

      // ── Merge gas back to parent ──────────────────────────────────────
      long dynTotal = State.RegionGasComposition.TotalUMoles[dynRegIdx];
      long parentTotal = State.RegionGasComposition.TotalUMoles[parentRegIdx];

      for (int g = 0; g < State.GasCount; g++)
      {
        int dynIdx = State.GetRegionUMoleIndex(g, dynRegIdx);
        int parentIdx = State.GetRegionUMoleIndex(g, parentRegIdx);

        State.RegionGasComposition.uMoles[parentIdx] += State.RegionGasComposition.uMoles[dynIdx];
        State.RegionGasComposition.uMoles[dynIdx] = 0;
      }

      State.RegionGasComposition.TotalUMoles[parentRegIdx] += dynTotal;
      State.RegionGasComposition.TotalUMoles[dynRegIdx] = 0;

      // ── Merge temperature (mole-weighted to conserve thermal energy) ──
      float totalMoles = (float)(dynTotal + parentTotal);
      if (totalMoles > 1e-6f)
      {
        float tDyn = State.RegionPhysicsBuffer.TemperatureK[dynRegIdx];
        float tParent = State.RegionPhysicsBuffer.TemperatureK[parentRegIdx];
        State.RegionPhysicsBuffer.TemperatureK[parentRegIdx] =
          (tDyn * dynTotal + tParent * parentTotal) / totalMoles;
      }

      // ── Restore parent volume and structural capacity ─────────────────
      float cellVol = State.WorldPhysicsBuffer.CellVolumes[worldIdx];
      State.RegionPhysicsBuffer.RegionVolumes[parentRegIdx] += cellVol;

      float dynECap = State.RegionPhysicsBuffer.EnclosingThermalCapacity[dynRegIdx];
      float parentECap = State.RegionPhysicsBuffer.EnclosingThermalCapacity[parentRegIdx];
      float totalECap = dynECap + parentECap;
      if (totalECap > 1e-6f)
      {
        float tDynE = State.RegionPhysicsBuffer.EnclosingTemperatureK[dynRegIdx];
        float tParentE = State.RegionPhysicsBuffer.EnclosingTemperatureK[parentRegIdx];
        float blended = (dynECap * tDynE + parentECap * tParentE) / totalECap;
        State.RegionPhysicsBuffer.EnclosingTemperatureK[parentRegIdx] = blended;
        State.RegionPhysicsBuffer.PreviousEnclosingTemperatureK[parentRegIdx] = blended;
      }

      float dynICap = State.RegionPhysicsBuffer.InternalMassThermalCapacity[dynRegIdx];
      float parentICap = State.RegionPhysicsBuffer.InternalMassThermalCapacity[parentRegIdx];
      float totalICap = dynICap + parentICap;
      if (totalICap > 1e-6f)
      {
        float tDynI = State.RegionPhysicsBuffer.InternalMassTemperatureK[dynRegIdx];
        float tParentI = State.RegionPhysicsBuffer.InternalMassTemperatureK[parentRegIdx];
        float blended = (dynICap * tDynI + parentICap * tParentI) / totalICap;
        State.RegionPhysicsBuffer.InternalMassTemperatureK[parentRegIdx] = blended;
        State.RegionPhysicsBuffer.PreviousInternalMassTemperatureK[parentRegIdx] = blended;
      }

      State.RegionPhysicsBuffer.EnclosingThermalConductance[parentRegIdx] +=
        State.RegionPhysicsBuffer.EnclosingThermalConductance[dynRegIdx];
      State.RegionPhysicsBuffer.InternalMassThermalConductance[parentRegIdx] +=
        State.RegionPhysicsBuffer.InternalMassThermalConductance[dynRegIdx];

      State.RegionPhysicsBuffer.EnclosingThermalCapacity[parentRegIdx] += dynECap;
      State.RegionPhysicsBuffer.InternalMassThermalCapacity[parentRegIdx] += dynICap;

      // Reset dynamic slot physics
      State.RegionPhysicsBuffer.RegionVolumes[dynRegIdx] = 0f;
      State.RegionPhysicsBuffer.EnclosingThermalCapacity[dynRegIdx] = 0f;
      State.RegionPhysicsBuffer.InternalMassThermalCapacity[dynRegIdx] = 0f;
      State.RegionPhysicsBuffer.EnclosingTemperatureK[dynRegIdx] = 0f;
      State.RegionPhysicsBuffer.InternalMassTemperatureK[dynRegIdx] = 0f;
      State.RegionPhysicsBuffer.PreviousEnclosingTemperatureK[dynRegIdx] = 0f;
      State.RegionPhysicsBuffer.PreviousInternalMassTemperatureK[dynRegIdx] = 0f;
      State.RegionPhysicsBuffer.EnclosingThermalConductance[dynRegIdx] = 0f;
      State.RegionPhysicsBuffer.InternalMassThermalConductance[dynRegIdx] = 0f;
      State.RegionPhysicsBuffer.TemperatureK[dynRegIdx] = 0f;

      // ── Tombstone dynamic faces (zero-flux sentinel-sentinel) ─────────
      int faceStart = State.DynamicRegions.FaceStart[slot];
      int faceCount = State.DynamicRegions.FaceCount[slot];
      for (int k = faceStart; k < faceStart + faceCount; k++)
      {
        State.DynamicFaces.RegionA[k] = State.SentinelRegionIndex;
        State.DynamicFaces.RegionB[k] = State.SentinelRegionIndex;
        State.DynamicFaces.SourceEdge[k] = -1;
      }

      // ── Mark slot free ────────────────────────────────────────────────
      State.RegionStates.IsActive[dynRegIdx] = false;
      State.RegionStates.ActiveTicks[dynRegIdx] = 0;
      State.RegionStates.EquilibriumTicks[dynRegIdx] = 0;
      State.RegionStates.RegionToRoomIndex[dynRegIdx] = -1;
      State.RegionStates.RegionToRoomID[dynRegIdx] = -1;
      State.DynamicRegions.Parent[slot] = -1;
      State.DynamicRegions.WorldIdx[slot] = -1;
      State.DynamicRegions.FaceStart[slot] = 0;
      State.DynamicRegions.FaceCount[slot] = 0;

      // ── Restore world-to-region lookup ────────────────────────────────
      State.GridLookups.WorldToRegionIndex[worldIdx] = parentRegIdx;

      State.DynRegionCount = Math.Max(0, State.DynRegionCount - 1);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private int FindFreeSlot()
    {
      for (int s = 0; s < AtmosphereGrid.MaxDynRegions; s++)
      {
        if (State.DynamicRegions.Parent[s] == -1)
          return s;
      }
      return -1;
    }

    private void ResizeDynFluxLists(int totalFaces)
    {
      int gasFluxLength = totalFaces * State.GasCount;
      int thermalFluxLength = totalFaces;

      if (State.DynamicFaces.GasFlux.Length != gasFluxLength)
        State.DynamicFaces.GasFlux.Resize(gasFluxLength, Unity.Collections.NativeArrayOptions.UninitializedMemory);

      if (State.DynamicFaces.TotalGasFlux.Length != totalFaces)
        State.DynamicFaces.TotalGasFlux.Resize(totalFaces, Unity.Collections.NativeArrayOptions.UninitializedMemory);

      if (State.DynamicFaces.ThermalFlux.Length != thermalFluxLength)
        State.DynamicFaces.ThermalFlux.Resize(thermalFluxLength, Unity.Collections.NativeArrayOptions.UninitializedMemory);
    }
  }
}
