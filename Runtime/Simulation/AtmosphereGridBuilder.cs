using SolarWeb.Pneuma.Data;
using SolarWeb.Pneuma.Gas;
using SolarWeb.Pneuma.Grid;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;

namespace SolarWeb.Pneuma.Simulation
{
  public static class AtmosphereGridBuilder
  {
    public static AtmosphereGrid RebuildSimulation(SimulationConfig config, GasRegistry gasRegistry, AtmosphereGrid? oldGrid = null, List<CellState>? savedStates = null, GridStateSnapshot? snapshot = null)
    {
      config.Ambient.Bake(gasRegistry);

      // Total region slots = static + dynamic pool + 1 sentinel
      int totalRegions = config.RegionCount + AtmosphereGrid.MaxDynRegions;

      AtmosphereGrid newGrid = new()
      {
        MapWidth = config.MapWidth,
        MapHeight = config.MapHeight,
        WorldCellCount = config.MapWidth * config.MapHeight,
        GasCount = config.Gases.Length,
        AmbientEnvironment = config.Ambient,
        SimCellCount = config.CellCount,
        SimRegionCount = config.RegionCount,
        DynamicRegionPoolStart = config.RegionCount,
        DynRegionCount = 0,
        SentinelRegionIndex = totalRegions, // = DynamicRegionPoolStart + MaxDynRegions
      };

      newGrid.Stride = (newGrid.SimCellCount + 1 + 15) & ~15;
      newGrid.RegionStride = (totalRegions + 1 + 15) & ~15; // includes sentinel

      int totalCellPairCount = config.RegionFaceCellSimA?.Count ?? 0;
      AllocateGridArrays(newGrid, config, config.RegionFaces.Count, totalCellPairCount);

      ExternalEnvironment.CopyToGrid(newGrid.AmbientEnvironment, newGrid, gasRegistry);

      // Build per-region face adjacency (static faces only; dynamic faces are in NativeLists)
      int[] tempRegionCounts = new int[newGrid.RegionStride];
      for (int f = 0; f < config.RegionFaces.Count; f++)
      {
        int a = config.RegionFaces[f].RegionA;
        int b = config.RegionFaces[f].RegionB;

        if (a >= 0 && a < newGrid.RegionStride)
          tempRegionCounts[a]++;

        if (b >= 0 && b < newGrid.RegionStride && b != newGrid.SentinelRegionIndex)
          tempRegionCounts[b]++;
      }

      int currentRegOffset = 0;
      for (int i = 0; i < newGrid.RegionStride; i++)
      {
        newGrid.RegionFaceBuffer.RegionFaceOffsets[i] = currentRegOffset;
        newGrid.RegionFaceBuffer.RegionFaceCounts[i] = 0;
        currentRegOffset += tempRegionCounts[i];
      }

      for (int f = 0; f < config.RegionFaces.Count; f++)
      {
        int a = config.RegionFaces[f].RegionA;
        int b = config.RegionFaces[f].RegionB;

        if (a >= 0 && a < newGrid.RegionStride)
        {
          int offsetA = newGrid.RegionFaceBuffer.RegionFaceOffsets[a];
          int indexInRegA = newGrid.RegionFaceBuffer.RegionFaceCounts[a]++;
          newGrid.RegionFaceBuffer.RegionFaceIndices[offsetA + indexInRegA] = f;
        }

        if (b >= 0 && b < newGrid.RegionStride && b != newGrid.SentinelRegionIndex)
        {
          int offsetB = newGrid.RegionFaceBuffer.RegionFaceOffsets[b];
          int indexInRegB = newGrid.RegionFaceBuffer.RegionFaceCounts[b]++;
          newGrid.RegionFaceBuffer.RegionFaceIndices[offsetB + indexInRegB] = f;
        }
      }

      for (int i = 0; i < newGrid.WorldCellCount; i++)
      {
        newGrid.GridLookups.WorldToSimIndex[i] = newGrid.SentinelCellIndex;
        newGrid.GridLookups.WorldToRegionIndex[i] = newGrid.SentinelRegionIndex;
        newGrid.WorldPhysicsBuffer.CellVolumes[i] = AtmosphereGrid.DefaultCellVolume;
      }

      if (config.CellThermalConductivity != null)
      {
        for (int i = 0; i < newGrid.WorldCellCount; i++)
        {
          newGrid.TopologyBuffer.CellThermalConductivity[i] = config.CellThermalConductivity[i];
          newGrid.TopologyBuffer.CellThermalCapacity[i] = config.CellThermalCapacity[i];
          newGrid.TopologyBuffer.CellStructuralConductance[i] = config.CellStructuralConductance[i];
          newGrid.TopologyBuffer.CellGasPermeability[i] = config.CellGasPermeability[i];
          newGrid.TopologyBuffer.CellFlowArea[i] = config.CellFlowArea[i];
          newGrid.TopologyBuffer.CellMaxPressureDeltaKpa[i] = config.CellMaxPressureDeltaKpa[i];
          newGrid.TopologyBuffer.CellMinColDia[i] = config.CellMinColDia[i];
          newGrid.TopologyBuffer.CellMaxColDia[i] = config.CellMaxColDia[i];
          newGrid.TopologyBuffer.CellPumpRate[i] = config.CellPumpRate[i];
          newGrid.TopologyBuffer.CellFlowDirX[i] = config.CellFlowDirX[i];
          newGrid.TopologyBuffer.CellFlowDirZ[i] = config.CellFlowDirZ[i];
          newGrid.TopologyBuffer.CellFlags[i] = config.CellFlags[i];
          newGrid.TopologyBuffer.CellTopPermeability[i] = config.CellTopPermeability[i];
          newGrid.TopologyBuffer.CellTopConductivity[i] = config.CellTopConductivity[i];
          newGrid.TopologyBuffer.CellBottomPermeability[i] = config.CellBottomPermeability[i];
          newGrid.TopologyBuffer.CellBottomConductivity[i] = config.CellBottomConductivity[i];
        }
      }

      if (config.LinkOffsets != null)
      {
        for (int i = 0; i < config.LinkOffsets.Count; i++)
          newGrid.TopologyBuffer.FaceLinkOffsets[i] = config.LinkOffsets[i];
        for (int i = 0; i < config.LinkCounts.Count; i++)
          newGrid.TopologyBuffer.FaceLinkCounts[i] = config.LinkCounts[i];
        for (int i = 0; i < config.LinkStartWorldIndices.Count; i++)
          newGrid.TopologyBuffer.FaceLinkWorldStart[i] = config.LinkStartWorldIndices[i];
        for (int i = 0; i < config.LinkDirections.Count; i++)
          newGrid.TopologyBuffer.FaceLinkDirection[i] = config.LinkDirections[i];
      }

      for (int i = 0; i < config.RegionCount; i++)
      {
        var regionData = config.RegionData[i];
        newGrid.RegionPhysicsBuffer.TemperatureK[i] = regionData.TemperatureK;
        newGrid.RegionPhysicsBuffer.StructuralTemperatureK[i] = regionData.StructuralTemperatureK;
        newGrid.RegionPhysicsBuffer.MaxPressureKpa[i] = regionData.MaxPressureKpa;
        newGrid.RegionPhysicsBuffer.PreviousTemperatureK[i] = regionData.TemperatureK;
        newGrid.RegionStates.RegionToRoomID[i] = regionData.RoomID;
        float totalVolume = 0;

        int minX = int.MaxValue, minZ = int.MaxValue;
        int maxX = int.MinValue, maxZ = int.MinValue;

        foreach (var cell in regionData.RegionCells)
        {
          totalVolume += newGrid.WorldPhysicsBuffer.CellVolumes[cell];
          newGrid.GridLookups.WorldToRegionIndex[cell] = i;

          int x = cell % config.MapWidth;
          int z = cell / config.MapWidth;
          minX = System.Math.Min(minX, x);
          minZ = System.Math.Min(minZ, z);
          maxX = System.Math.Max(maxX, x);
          maxZ = System.Math.Max(maxZ, z);
        }

        newGrid.RegionPhysicsBuffer.RegionVolumes[i] = totalVolume;
        newGrid.RegionStates.MinX[i] = minX;
        newGrid.RegionStates.MinZ[i] = minZ;
        newGrid.RegionStates.MaxX[i] = maxX;
        newGrid.RegionStates.MaxZ[i] = maxZ;
      }

      for (int f = 0; f < config.RegionFaces.Count; f++)
      {
        var link = config.RegionFaces[f];
        newGrid.RegionFaceBuffer.FaceRegionA[f] = link.RegionA;
        newGrid.RegionFaceBuffer.FaceRegionB[f] = link.RegionB;
        newGrid.RegionFaceBuffer.FaceType[f] = link.FaceType;
        newGrid.RegionFaceBuffer.FaceGasPermeability[f] = link.TotalPermeability;
        newGrid.RegionFaceBuffer.FaceMaxPressureDeltaKpa[f] = link.MaxPressureDeltaKpa;
        newGrid.RegionFaceBuffer.FaceThermalConductivity[f] = link.TotalConductance;
        newGrid.RegionFaceBuffer.FaceSurfaceArea[f] = link.TotalSurfaceArea;
        newGrid.RegionFaceBuffer.FaceDirection[f] = link.Direction;
        newGrid.RegionFaceBuffer.FacePressureOffsetKpa[f] = 0f;

        newGrid.RegionFaceBuffer.MinCollisionDiameter[f] = link.MinCollisionDiameter;
        newGrid.RegionFaceBuffer.MaxCollisionDiameter[f] = link.MaxCollisionDiameter;
        newGrid.RegionFaceBuffer.FaceFlowDirection[f] = link.FlowDirection;
        newGrid.RegionFaceBuffer.FaceActivePumpRate[f] = link.ActivePumpRate;
        newGrid.RegionFaceBuffer.FaceMaxPumpPressureKpa[f] = link.MaxPumpPressureKpa;
        newGrid.RegionFaceBuffer.FaceRegulatorKpa[f] = link.RegulatorKpa;
        newGrid.RegionFaceBuffer.FaceWindExposureX[f] = link.WindExposureX;
        newGrid.RegionFaceBuffer.FaceWindExposureZ[f] = link.WindExposureZ;
      }

      if (config.RegionFaceCellSimA != null)
      {
        for (int f = 0; f < config.RegionFaces.Count; f++)
        {
          newGrid.RegionFaceBuffer.RegionFaceToCellOffsets[f] = config.RegionFaceCellPairOffsets[f];
          newGrid.RegionFaceBuffer.RegionFaceToCellCounts[f] = config.RegionFaceCellPairCounts[f];
        }
        for (int p = 0; p < config.RegionFaceCellSimA.Count; p++)
        {
          newGrid.RegionFaceBuffer.RegionFaceToCellSimA[p] = config.RegionFaceCellSimA[p];
          newGrid.RegionFaceBuffer.RegionFaceToCellSimB[p] = config.RegionFaceCellSimB[p];
        }
      }

      for (int i = 0; i < config.CellCount; i++)
      {
        var cell = config.CellData[i];
        newGrid.GridLookups.SimToWorldIndex[i] = cell.WorldIndex;
        newGrid.GridLookups.WorldToSimIndex[cell.WorldIndex] = i;
        newGrid.WorldPhysicsBuffer.CellVolumes[cell.WorldIndex] = cell.CellProperties.Volume;

        ApplyStructuralProperties(newGrid, i, cell);

        int regIdx = newGrid.GridLookups.WorldToRegionIndex[cell.WorldIndex];
        if (regIdx >= 0 && regIdx < newGrid.SimRegionCount)
        {
          newGrid.RegionPhysicsBuffer.StructuralThermalCapacity[regIdx] += cell.CellProperties.ThermalCapacity;
          newGrid.RegionPhysicsBuffer.StructuralThermalConductance[regIdx] += cell.CellProperties.ThermalConductivity * 2.5f;
        }
      }

      for (int i = 0; i < config.RegionCount; i++)
      {
        newGrid.RegionPhysicsBuffer.StructuralTemperatureK[i] = newGrid.RegionPhysicsBuffer.TemperatureK[i];
        newGrid.RegionPhysicsBuffer.PreviousStructuralTemperatureK[i] = newGrid.RegionPhysicsBuffer.TemperatureK[i];
      }

      // Ensure sentinel and dynamic pool are also initialized to ambient
      newGrid.RegionPhysicsBuffer.StructuralTemperatureK[newGrid.SentinelRegionIndex] = newGrid.AmbientEnvironment.TemperatureK;
      newGrid.RegionPhysicsBuffer.PreviousStructuralTemperatureK[newGrid.SentinelRegionIndex] = newGrid.AmbientEnvironment.TemperatureK;

      // Recompute all region faces now that the grid is built, ensuring consistency
      // between initialization and dynamic updates.
      if (config.RegionFaces.Count > 0)
      {
        NativeArray<int> allFaces = new NativeArray<int>(config.RegionFaces.Count, Allocator.TempJob);
        for (int i = 0; i < config.RegionFaces.Count; i++) allFaces[i] = i;

        new SolarWeb.Pneuma.Jobs.Diffusion.RecomputeDirtyFaceMetrics
        {
          DirtyFaceIndices = allFaces,
          CellThermalConductivity = newGrid.TopologyBuffer.CellThermalConductivity,
          CellGasPermeability = newGrid.TopologyBuffer.CellGasPermeability,
          CellMaxPressureDeltaKpa = newGrid.TopologyBuffer.CellMaxPressureDeltaKpa,
          CellMinColDia = newGrid.TopologyBuffer.CellMinColDia,
          CellMaxColDia = newGrid.TopologyBuffer.CellMaxColDia,
          CellPumpRate = newGrid.TopologyBuffer.CellPumpRate,
          CellFlowDirX = newGrid.TopologyBuffer.CellFlowDirX,
          CellFlowDirZ = newGrid.TopologyBuffer.CellFlowDirZ,
          CellFlags = newGrid.TopologyBuffer.CellFlags,

          CellTopPermeability = newGrid.TopologyBuffer.CellTopPermeability,
          CellTopConductivity = newGrid.TopologyBuffer.CellTopConductivity,
          CellBottomPermeability = newGrid.TopologyBuffer.CellBottomPermeability,
          CellBottomConductivity = newGrid.TopologyBuffer.CellBottomConductivity,

          WorldToRegionIndex = newGrid.GridLookups.WorldToRegionIndex,
          SimToWorldIndex = newGrid.GridLookups.SimToWorldIndex,
          FaceLinkOffsets = newGrid.TopologyBuffer.FaceLinkOffsets,
          FaceLinkCounts = newGrid.TopologyBuffer.FaceLinkCounts,
          FaceLinkWorldStart = newGrid.TopologyBuffer.FaceLinkWorldStart,
          FaceLinkDirection = newGrid.TopologyBuffer.FaceLinkDirection,

          FaceType = newGrid.RegionFaceBuffer.FaceType,
          RegionFaceToCellOffsets = newGrid.RegionFaceBuffer.RegionFaceToCellOffsets,
          RegionFaceToCellCounts = newGrid.RegionFaceBuffer.RegionFaceToCellCounts,
          RegionFaceToCellSimA = newGrid.RegionFaceBuffer.RegionFaceToCellSimA,

          MapWidth = newGrid.MapWidth,
          MapHeight = newGrid.MapHeight,
          SentinelRegionIndex = newGrid.SentinelRegionIndex,
          FaceGasPermeability = newGrid.RegionFaceBuffer.FaceGasPermeability,
          FaceThermalConductivity = newGrid.RegionFaceBuffer.FaceThermalConductivity,
          FaceMaxPressureDeltaKpa = newGrid.RegionFaceBuffer.FaceMaxPressureDeltaKpa,
          MinCollisionDiameter = newGrid.RegionFaceBuffer.MinCollisionDiameter,
          MaxCollisionDiameter = newGrid.RegionFaceBuffer.MaxCollisionDiameter,
          FaceActivePumpRate = newGrid.RegionFaceBuffer.FaceActivePumpRate,
          FaceFlowDirection = newGrid.RegionFaceBuffer.FaceFlowDirection
        }.Schedule(allFaces.Length, 8).Complete();
        allFaces.Dispose();
      }

      // Clear all starting gas before applying saved/existing state.
      for (int i = 0; i < newGrid.SimRegionCount; i++)
      {
        newGrid.RegionGasComposition.TotalUMoles[i] = 0;
        for (int g = 0; g < newGrid.GasCount; g++)
        {
          int regIdx = newGrid.GetRegionUMoleIndex(g, i);
          newGrid.RegionGasComposition.uMoles[regIdx] = 0;
          newGrid.RegionGasComposition.PendinguMolesDelta[regIdx] = 0;
          newGrid.SolidComposition.uMoles[regIdx] = 0;
          newGrid.SolidComposition.PendinguMolesDelta[regIdx] = 0;
          newGrid.SolidComposition.PreviousuMoles[regIdx] = 0;
          newGrid.LiquidComposition.uMoles[regIdx] = 0;
          newGrid.LiquidComposition.PendinguMolesDelta[regIdx] = 0;
          newGrid.LiquidComposition.PreviousuMoles[regIdx] = 0;
        }
      }

      ApplySavedState(newGrid, config, savedStates, snapshot);

      // Populate static cell-face topology cache in GridLookups.
      // This is used by RegionGraphMutator to create dynamic region faces at runtime.
      {
        int cellFaceCount = config.CellFaces.Count;

        for (int f = 0; f < cellFaceCount; f++)
        {
          FaceLink link = config.CellFaces[f];
          newGrid.GridLookups.CellFaceCellA[f] = link.CellA;
          newGrid.GridLookups.CellFaceCellB[f] = link.CellB;
          newGrid.GridLookups.CellFacePermeability[f] = link.Props.FaceGasPermeability;
          newGrid.GridLookups.CellFaceConductivity[f] = link.Props.ThermalConductivity;
          newGrid.GridLookups.CellFaceSurfaceArea[f] = link.SurfaceArea;
          newGrid.GridLookups.CellFaceMinCollisionDiameter[f] = link.MinCollisionDiameter;
          newGrid.GridLookups.CellFaceMaxCollisionDiameter[f] = link.MaxCollisionDiameter;
          newGrid.GridLookups.CellFaceDirection[f] = link.FlowDirection;
        }

        // Build per-cell face adjacency.
        int[] tempCellCounts = new int[newGrid.Stride];
        for (int f = 0; f < cellFaceCount; f++)
        {
          tempCellCounts[config.CellFaces[f].CellA]++;
          if (config.CellFaces[f].CellB != newGrid.SentinelCellIndex)
            tempCellCounts[config.CellFaces[f].CellB]++;
        }

        int currentCellOffset = 0;
        for (int i = 0; i < newGrid.SimCellCount; i++)
        {
          newGrid.GridLookups.CellFaceOffsets[i] = currentCellOffset;
          newGrid.GridLookups.CellFaceCounts[i] = 0;
          currentCellOffset += tempCellCounts[i];
        }

        for (int f = 0; f < cellFaceCount; f++)
        {
          int a = config.CellFaces[f].CellA;
          int b = config.CellFaces[f].CellB;

          int offsetA = newGrid.GridLookups.CellFaceOffsets[a];
          int indexInA = newGrid.GridLookups.CellFaceCounts[a]++;
          newGrid.GridLookups.CellFaceIndices[offsetA + indexInA] = f;

          if (b != newGrid.SentinelCellIndex)
          {
            int offsetB = newGrid.GridLookups.CellFaceOffsets[b];
            int indexInB = newGrid.GridLookups.CellFaceCounts[b]++;
            newGrid.GridLookups.CellFaceIndices[offsetB + indexInB] = f;
          }
        }
      }

      oldGrid?.Dispose();
      return newGrid;
    }

    private static void ApplySavedState(AtmosphereGrid newGrid, SimulationConfig config, List<CellState>? savedStates, GridStateSnapshot? snapshot = null)
    {
      bool[] cellReceivedState = new bool[newGrid.WorldCellCount];

      if (savedStates != null)
      {
        foreach (var savedState in savedStates)
        {
          savedState.ApplyToGrid(newGrid);
          if (savedState.HasValidPriorState) cellReceivedState[savedState.WorldPosition] = true;
        }
      }

      if (config.SavedRegions != null && snapshot == null)
      {
        for (int i = 0; i < config.RegionCount; i++)
        {
          int minX = newGrid.RegionStates.MinX[i];
          int minZ = newGrid.RegionStates.MinZ[i];
          int maxX = newGrid.RegionStates.MaxX[i];
          int maxZ = newGrid.RegionStates.MaxZ[i];

          foreach (var saved in config.SavedRegions)
          {
            if (saved.MinX == minX && saved.MinZ == minZ && saved.MaxX == maxX && saved.MaxZ == maxZ)
            {
              newGrid.RegionStates.IsBurning[i] = saved.IsBurning;
              newGrid.RegionStates.BurnIntensity[i] = saved.BurnIntensity;
              newGrid.RegionPhysicsBuffer.TemperatureK[i] = saved.TemperatureK;
              float st = (saved.StructuralTemperatureK > 1f) ? saved.StructuralTemperatureK : saved.TemperatureK;
              newGrid.RegionPhysicsBuffer.StructuralTemperatureK[i] = st;
              newGrid.RegionPhysicsBuffer.PreviousStructuralTemperatureK[i] = st; for (int g = 0; g < System.Math.Min(newGrid.GasCount, saved.GasUMoles.Length); g++)
              {
                int regIdx = newGrid.GetRegionUMoleIndex(g, i);
                newGrid.RegionGasComposition.uMoles[regIdx] = saved.GasUMoles[g];
                newGrid.RegionGasComposition.TotalUMoles[i] += saved.GasUMoles[g];
                if (saved.SolidUMoles != null && g < saved.SolidUMoles.Length) newGrid.SolidComposition.uMoles[regIdx] = saved.SolidUMoles[g];
                if (saved.LiquidUMoles != null && g < saved.LiquidUMoles.Length) newGrid.LiquidComposition.uMoles[regIdx] = saved.LiquidUMoles[g];
              }
              break;
            }
          }
        }
      }

      if (snapshot != null)
        TransferStateFromSnapshot(newGrid, snapshot);

      for (int wIdx = 0; wIdx < newGrid.WorldCellCount; wIdx++)
      {
        if (cellReceivedState[wIdx]) continue;

        int regIdx = newGrid.GridLookups.WorldToRegionIndex[wIdx];
        if (regIdx < 0 || regIdx == newGrid.SentinelRegionIndex) continue;
        if (newGrid.RegionGasComposition.TotalUMoles[regIdx] > 0) continue;

        float regionVolume = newGrid.RegionPhysicsBuffer.RegionVolumes[regIdx];
        float initialTemp = newGrid.RegionPhysicsBuffer.TemperatureK[regIdx];

        if (initialTemp <= 0.1f) initialTemp = 293.15f;

        for (int g = 0; g < newGrid.GasCount; g++)
        {
          var proportion = newGrid.AmbientEnvironment.GasProportionsById[g];
          var totalPressurePa = newGrid.AmbientEnvironment.TotalPressureKpa * 1000.0;

          // Use the room's target temperature to calculate moles so it starts at 1 ATM (or ambient pressure)
          var moles = MathA.AtmosphereCalc.IdealGasLaw(totalPressurePa, regionVolume, initialTemp) * proportion;
          long uMoles = (long)(moles * 1_000_000);

          newGrid.RegionGasComposition.uMoles[newGrid.GetRegionUMoleIndex(g, regIdx)] += uMoles;
          newGrid.RegionGasComposition.TotalUMoles[regIdx] += uMoles;
        }
      }
    }

    private static void AllocateGridArrays(AtmosphereGrid grid, SimulationConfig config, int regionFaceCount, int totalCellPairCount)
    {
      int cellFaceCount = config.CellFaces.Count;
      int cellAdjSlots = cellFaceCount * 2;

      grid.GridLookups.Initialize(grid.MapHeight * grid.MapWidth, grid.Stride, cellFaceCount, cellAdjSlots);
      grid.WorldPhysicsBuffer.Initialize(grid.MapHeight * grid.MapWidth);

      int regionFaceStride = (regionFaceCount + 15) & ~15;
      grid.TopologyBuffer.Initialize(grid.MapHeight * grid.MapWidth, grid.Stride, regionFaceStride, config.LinkDirections?.Count ?? 0);
      grid.RegionPhysicsBuffer.Initialize(grid.GasCount, grid.RegionStride);
      grid.RegionGasComposition.Initialize(grid.RegionStride, grid.GasCount);
      grid.RegionFaceBuffer.Initialize(regionFaceCount, regionFaceStride, grid.GasCount, grid.RegionStride, totalCellPairCount);
      grid.RegionStates.Initialize(grid.RegionStride, grid.Stride);
      grid.LiquidComposition.Initialize(grid.GasCount, grid.RegionStride);
      grid.SolidComposition.Initialize(grid.GasCount, grid.RegionStride);

      grid.GasStoichiometry.Initialize(grid.GasCount, config.TotalAtomCount);
      grid.PlantMetabolismRegistry.Initialize(config.PlantProfileCount);
      grid.RegionPlantPopulation.Initialize(grid.RegionStride, config.PlantProfileCount);
      grid.RegionPlantStates.Initialize(grid.RegionStride, config.PlantProfileCount);

      // Initialize high-level dynamic and event buffers
      grid.DynamicRegions.Initialize(AtmosphereGrid.MaxDynRegions);
      grid.DynamicFaces.Initialize(64, grid.GasCount);
      grid.Events.Initialize();

      // Initialize structural properties to zero for aggregation
      for (int i = 0; i < grid.RegionStride; i++)
      {
        grid.RegionPhysicsBuffer.StructuralThermalCapacity[i] = 0f;
        grid.RegionPhysicsBuffer.StructuralThermalConductance[i] = 0f;
      }
    }

    public static void ApplyStructuralProperties(AtmosphereGrid grid, int simIdx, CellInitializationData cell)
    {
      grid.WorldPhysicsBuffer.CellVolumes[cell.WorldIndex] = cell.CellProperties.Volume;
    }

    public static int[] BuildWorldSimLookups(SimulationConfig config)
    {
      int[] worldToSimIndex = new int[config.MapWidth * config.MapHeight];
      int sentinelCellIndex = config.CellCount;

      for (int i = 0; i < worldToSimIndex.Length; i++)
        worldToSimIndex[i] = sentinelCellIndex;

      for (int i = 0; i < config.CellCount; i++)
      {
        var cellData = config.CellData[i];
        worldToSimIndex[cellData.WorldIndex] = i;
      }

      return worldToSimIndex;
    }

    /// <summary>
    /// Distributes gas and temperature from a pre-rebuild snapshot into the new grid topology.
    /// Gas is transferred count-proportionally (preserving mass). Temperature is set as a
    /// volume-weighted average of the old regions that contributed cells to each new region.
    /// RoomCurrentTemperatureK is also initialized so the first CalculateRoomTemperatureDeltas
    /// tick sees a correct (near-zero) delta rather than a spurious spike from a zero baseline.
    /// </summary>
    private static void TransferStateFromSnapshot(AtmosphereGrid newGrid, GridStateSnapshot snap)
    {
      int newRegionCount = newGrid.SimRegionCount;
      float[] weightedTemp = new float[newGrid.RegionStride];
      float[] weightedStructTemp = new float[newGrid.RegionStride];
      float[] totalVol = new float[newGrid.RegionStride];
      float[] maxBurnIntensity = new float[newGrid.RegionStride];

      int worldLimit = System.Math.Min(snap.WorldCellCount, newGrid.WorldCellCount);
      for (int wIdx = 0; wIdx < worldLimit; wIdx++)
      {
        int oldR = snap.WorldToRegionIndex[wIdx];
        if (oldR < 0 || oldR >= snap.SimRegionCount) continue;

        int newR = newGrid.GridLookups.WorldToRegionIndex[wIdx];
        if (newR < 0 || newR == newGrid.SentinelRegionIndex) continue;

        int cellCount = snap.RegionCellCounts[oldR];
        if (cellCount <= 0) continue;

        // Gas: count-proportional share (preserves total mass across the region)
        for (int g = 0; g < snap.GasCount; g++)
        {
          int oldIdx = g * snap.RegionStride + oldR;
          int newIdx = newGrid.GetRegionUMoleIndex(g, newR);
          long gasShare = snap.FlatUMoles[oldIdx] / cellCount;
          long solidShare = snap.FlatSolidUMoles[oldIdx] / cellCount;
          long liquidShare = snap.FlatLiquidUMoles[oldIdx] / cellCount;
          newGrid.RegionGasComposition.uMoles[newIdx] += gasShare;
          newGrid.RegionGasComposition.TotalUMoles[newR] += gasShare;
          newGrid.SolidComposition.uMoles[newIdx] += solidShare;
          newGrid.LiquidComposition.uMoles[newIdx] += liquidShare;
        }

        // Temperature: volume-weighted accumulation across contributing old regions
        float cellVol = newGrid.WorldPhysicsBuffer.CellVolumes[wIdx];
        weightedTemp[newR] += snap.TemperatureK[oldR] * cellVol;
        weightedStructTemp[newR] += snap.StructuralTemperatureK[oldR] * cellVol;
        totalVol[newR] += cellVol;

        // Burn state: track the maximum intensity seen from any contributing old region
        if (snap.IsBurning[oldR])
        {
          newGrid.RegionStates.IsBurning[newR] = true;
          maxBurnIntensity[newR] = System.Math.Max(maxBurnIntensity[newR], snap.BurnIntensity[oldR]);
        }
      }

      // Finalize per-region values for all regions that received cells (static and dynamic)
      for (int r = 0; r < newGrid.RegionStride; r++)
      {
        if (totalVol[r] <= 0f) continue;

        float t = weightedTemp[r] / totalVol[r];
        float st = weightedStructTemp[r] / totalVol[r];
        newGrid.RegionPhysicsBuffer.TemperatureK[r] = t;
        newGrid.RegionPhysicsBuffer.PreviousTemperatureK[r] = t;
        newGrid.RegionPhysicsBuffer.StructuralTemperatureK[r] = st;
        newGrid.RegionPhysicsBuffer.PreviousStructuralTemperatureK[r] = st;
        newGrid.RegionPhysicsBuffer.RoomCurrentTemperatureK[r] = t;
        if (newGrid.RegionStates.IsBurning[r])
          newGrid.RegionStates.BurnIntensity[r] = maxBurnIntensity[r];
      }
    }

    public static void UpdateRegionFaceProperties(AtmosphereGrid grid, int faceIdx, float permeability, float thermalConductivity, float minColDia, float maxColDia, sbyte flowDir, float pumpRate, float maxPumpPressureKpa = 0f, float regulatorKpa = -1f)
    {
      grid.RegionFaceBuffer.FaceGasPermeability[faceIdx] = permeability;
      grid.RegionFaceBuffer.FaceThermalConductivity[faceIdx] = thermalConductivity;
      grid.RegionFaceBuffer.MinCollisionDiameter[faceIdx] = minColDia;
      grid.RegionFaceBuffer.MaxCollisionDiameter[faceIdx] = maxColDia;
      grid.RegionFaceBuffer.FaceFlowDirection[faceIdx] = flowDir;
      grid.RegionFaceBuffer.FaceActivePumpRate[faceIdx] = pumpRate;
      grid.RegionFaceBuffer.FaceMaxPumpPressureKpa[faceIdx] = maxPumpPressureKpa;
      grid.RegionFaceBuffer.FaceRegulatorKpa[faceIdx] = regulatorKpa;

      int regA = grid.RegionFaceBuffer.FaceRegionA[faceIdx];
      int regB = grid.RegionFaceBuffer.FaceRegionB[faceIdx];

      if (regA != -1 && regA != grid.SentinelRegionIndex) grid.RegionStates.ActiveTicks[regA] = 10;
      if (regB != -1 && regB != grid.SentinelRegionIndex) grid.RegionStates.ActiveTicks[regB] = 10;
    }
  }
}