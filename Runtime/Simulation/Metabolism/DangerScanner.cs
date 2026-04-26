using SolarWeb.Pneuma.Grid;
using SolarWeb.Pneuma.Jobs.Metabolism;
using Unity.Jobs;

namespace SolarWeb.Pneuma.Simulation
{
  public class DangerScanner
  {
    private AtmosphereGrid State => manager.State;
    private readonly AtmosphereManager manager;

    public DangerScanner(AtmosphereManager manager)
    {
      this.manager = manager;
    }

    public JobHandle DoComputeDanger(JobHandle dependency = default, bool updateBiological = true)
    {
      if (manager.Metabolism.Batches.Count == 0) return dependency;

      // Reset cell-level danger buffers before sync to prevent stale values from inactive cells persisting.
      manager.Metabolism.ClearCellDangerBuffers();

      var handle = new ComputeMetabolismDanger
      {
        ActiveRegionIndices = State.RegionStates.ActiveIndices.AsDeferredJobArray(),
        uMoles = State.RegionGasComposition.uMoles,
        TotalUMoles = State.RegionGasComposition.TotalUMoles,
        PressureKpa = State.RegionGasComposition.PressureKpa,
        TemperatureK = State.RegionPhysicsBuffer.TemperatureK,

        LowerExplosiveLimit = manager.GasRegistry.GasData.LowerExplosiveLimit,
        UpperExplosiveLimit = manager.GasRegistry.GasData.UpperExplosiveLimit,
        OxidizingPotency = manager.GasRegistry.GasData.OxidizingPotency,
        Radioactivity = manager.GasRegistry.GasData.Radioactivity,
        Corrosiveness = manager.GasRegistry.GasData.Corrosiveness,
        MolarHeatCapacityCp = manager.GasRegistry.GasData.MolarHeatCapacityCp,
        FuelGasMask = manager.GasRegistry.GasData.FuelGasMask,

        BatchDangerSpecs = manager.Metabolism.BatchDangerSpecs,
        BatchGasDangerMatrix = manager.Metabolism.BatchGasDangerMatrix,
        BatchRelevantGasMasks = manager.Metabolism.BatchRelevantGasMasks,
        BatchRequiredGasMasks = manager.Metabolism.BatchRequiredGasMasks,
        DangerLevels = manager.Metabolism.DangerLevels,
        PhysicalHazardLevels = manager.Metabolism.PhysicalHazardLevels,

        BatchCount = manager.Metabolism.PawnBatchCount,
        GasCount = State.GasCount,
        RegionStride = State.RegionStride,
        SentinelRegionIndex = State.SentinelRegionIndex,
        UpdateBiological = updateBiological
      }.Schedule(State.RegionStates.ActiveIndices, 32, dependency);

      // Map region-level danger results to cell-level buffers for UI and pathfinding
      var syncHandle = new SyncRegionDangerToCells
      {
        ActiveCellIndices = State.RegionStates.ActiveCellIndices.AsDeferredJobArray(),
        SimToWorldIndex = State.GridLookups.SimToWorldIndex,
        WorldToRegionIndex = State.GridLookups.WorldToRegionIndex,
        DangerLevels = manager.Metabolism.DangerLevels,
        PhysicalHazardLevels = manager.Metabolism.PhysicalHazardLevels,
        CellDangerLevels = manager.Metabolism.CellDangerLevels,
        CellPhysicalHazardLevels = manager.Metabolism.CellPhysicalHazardLevels,
        BatchCount = manager.Metabolism.PawnBatchCount,
        Stride = State.Stride,
        RegionStride = State.RegionStride,
        SentinelRegionIndex = State.SentinelRegionIndex,
        SentinelCellIndex = State.SentinelCellIndex
      }.Schedule(State.RegionStates.ActiveCellIndices, 32, handle);

      return AtmosphereManager.CompleteIfNeeded(syncHandle);
    }
  }
}
