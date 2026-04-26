using SolarWeb.Pneuma.Constants;
using SolarWeb.Pneuma.Grid;

namespace SolarWeb.Pneuma.MathA
{
  public static class PressureCalc
  {
    public static float CalculatePressure(AtmosphereGrid grid, int worldIdx)
    {
      if (worldIdx < 0 || worldIdx >= grid.GridLookups.WorldToRegionIndex.Length)
        return grid.AmbientEnvironment.TotalPressureKpa;

      int regIdx = grid.GridLookups.WorldToRegionIndex[worldIdx];
      if (regIdx < 0 || regIdx == grid.SentinelRegionIndex)
        return grid.AmbientEnvironment.TotalPressureKpa;

      return grid.RegionGasComposition.PressureKpa[regIdx];
    }

    public static float GetPressureKpa(AtmosphereGrid grid, int x, int z)
    {
      int worldIdx = (z * grid.MapWidth) + x;
      return CalculatePressure(grid, worldIdx);
    }

    public static float GetPressureNormalized(AtmosphereGrid grid, int x, int z)
    {
      return GetPressureKpa(grid, x, z) / AtmosphereConstants.StandardAtmosphereKpa;
    }
  }
}
