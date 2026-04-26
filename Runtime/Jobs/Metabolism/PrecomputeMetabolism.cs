using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace SolarWeb.Pneuma.Jobs.Metabolism
{
  /// <summary>
  /// Per-entity precompute step that runs before the plasma-diffusion jobs.
  /// Reads the region environment each entity occupies and derives scalars used
  /// downstream to make metabolism respond to actual gas conditions.
  ///
  /// Currently computes:
  ///   EntitySolubilityScale — Henry's law temperature correction for plasma solubility.
  ///     scale = BodyTemperature_K / T_region
  ///     scale > 1.0 at cold temperatures (gas more soluble in plasma)
  ///     scale < 1.0 at elevated temperatures (gas less soluble in plasma)
  ///     Applied as a multiplier on free-gas uptake factors in PlasmaDiffusion.
  /// </summary>
  [BurstCompile]
  public struct PrecomputeMetabolism : IJobParallelFor
  {
    [ReadOnly] public NativeArray<int> WorldIndices;
    [ReadOnly] public NativeArray<int> WorldToRegionIndex;
    [ReadOnly] public NativeArray<float> RegionTemperatureK;

    /// <summary>Reference body temperature [K] (≈ 310 K = 37 °C).
    /// EntitySolubilityScale equals 1.0 when region temperature matches this value.</summary>
    public float BodyTemperature_K;

    public NativeArray<float> EntitySolubilityScale;

    public void Execute(int i)
    {
      int worldIdx = WorldIndices[i];
      float cellTemp;

      if (worldIdx >= 0 && worldIdx < WorldToRegionIndex.Length)
      {
        int regIdx = WorldToRegionIndex[worldIdx];
        cellTemp = (regIdx >= 0 && regIdx < RegionTemperatureK.Length)
          ? RegionTemperatureK[regIdx]
          : BodyTemperature_K;
      }
      else
      {
        cellTemp = BodyTemperature_K;
      }

      EntitySolubilityScale[i] = BodyTemperature_K / math.max(cellTemp, 1f);
    }
  }
}
