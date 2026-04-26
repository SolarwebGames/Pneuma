using SolarWeb.Pneuma.Grid;

namespace SolarWeb.Pneuma.Data
{
  /// <summary>
  /// Compact capture of the old grid's region-level state for use during runtime topology rebuilds.
  /// Replaces the per-cell RegionalSlices approach: instead of materializing one CellState per world
  /// cell, we copy the region arrays directly and let the transfer step distribute gas and temperature
  /// into the new topology on a per-cell basis.
  /// </summary>
  public class GridStateSnapshot
  {
    public int[] WorldToRegionIndex = System.Array.Empty<int>();
    public float[] TemperatureK = System.Array.Empty<float>();
    public float[] StructuralTemperatureK = System.Array.Empty<float>();
    public long[] FlatUMoles = System.Array.Empty<long>();
    public long[] FlatSolidUMoles = System.Array.Empty<long>();
    public long[] FlatLiquidUMoles = System.Array.Empty<long>();
    public bool[] IsBurning = System.Array.Empty<bool>();
    public float[] BurnIntensity = System.Array.Empty<float>();
    public int[] RegionCellCounts = System.Array.Empty<int>();
    public int WorldCellCount;
    public int SimRegionCount;     // = DynamicRegionPoolStart (static regions only)
    public int GasCount;
    public int RegionStride;

    public static GridStateSnapshot Capture(AtmosphereGrid grid)
    {
      int worldCount = grid.WorldCellCount;
      int simRegions = grid.SimRegionCount;
      int regStride = grid.RegionStride;

      // Precompute how many world cells map to each old static region
      int[] counts = new int[regStride];
      var wtr = grid.GridLookups.WorldToRegionIndex;
      for (int i = 0; i < worldCount; i++)
      {
        int r = wtr[i];
        if (r >= 0 && r < simRegions)
          counts[r]++;
      }

      return new GridStateSnapshot
      {
        WorldToRegionIndex = wtr.ToArray(),
        TemperatureK = grid.RegionPhysicsBuffer.TemperatureK.ToArray(),
        StructuralTemperatureK = grid.RegionPhysicsBuffer.StructuralTemperatureK.ToArray(),
        FlatUMoles = grid.RegionGasComposition.uMoles.ToArray(),
        FlatSolidUMoles = grid.SolidComposition.uMoles.ToArray(),
        FlatLiquidUMoles = grid.LiquidComposition.uMoles.ToArray(),
        IsBurning = grid.RegionStates.IsBurning.ToArray(),
        BurnIntensity = grid.RegionStates.BurnIntensity.ToArray(),
        RegionCellCounts = counts,
        WorldCellCount = worldCount,
        SimRegionCount = simRegions,
        GasCount = grid.GasCount,
        RegionStride = regStride,
      };
    }
  }
}
