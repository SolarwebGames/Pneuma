using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

using SolarWeb.Pneuma.Data;

namespace SolarWeb.Pneuma.Jobs.UI
{
  [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
  public struct PrepareAmbientGasVFXJob : IJob
  {
    [ReadOnly] public NativeArray<int> ActiveRegionIndices;
    [ReadOnly] public NativeArray<float> MolarFractions; // Stride * GasCount
    [ReadOnly] public NativeArray<long> TotalUMoles;
    [ReadOnly] public NativeArray<float3> GasOverlayColor;
    [ReadOnly] public NativeArray<float> GasMolarMass;
    [ReadOnly] public NativeArray<int> RegionMinX;
    [ReadOnly] public NativeArray<int> RegionMaxX;
    [ReadOnly] public NativeArray<int> RegionMinZ;
    [ReadOnly] public NativeArray<int> RegionMaxZ;

    public NativeList<AmbientGasPointData> OutAmbientPoints;

    public int ActiveRegionCount;
    public int GasCount;
    public int RegionStride;
    public float VisibilityThresholdMoles;

    public void Execute()
    {
      for (int i = 0; i < ActiveRegionCount; i++)
      {
        int rIdx = ActiveRegionIndices[i];
        float totalMoles = TotalUMoles[rIdx] / 1e6f;
        if (totalMoles < 1.0f) continue; // Vacuum or near-vacuum

        float maxFrac = -1f;
        int maxGasIdx = -1;

        for (int g = 0; g < GasCount; g++)
        {
          float frac = MolarFractions[g * RegionStride + rIdx];
          if (frac > maxFrac)
          {
            maxFrac = frac;
            maxGasIdx = g;
          }
        }

        if (maxGasIdx == -1) continue;

        // Only emit if it's significant or not just standard air
        // (Actually, let's always emit if total moles are high enough, but scale intensity)

        float3 color = GasOverlayColor[maxGasIdx];
        float molarMass = GasMolarMass[maxGasIdx];

        float centerX = (RegionMinX[rIdx] + RegionMaxX[rIdx] + 1) * 0.5f;
        float centerZ = (RegionMinZ[rIdx] + RegionMaxZ[rIdx] + 1) * 0.5f;

        // Intensity based on total moles and potentially fraction
        float intensity = math.saturate(totalMoles / 100f); // 100 moles per cell approx standard

        OutAmbientPoints.Add(new AmbientGasPointData
        {
          Position = new float3(centerX, 0, centerZ),
          Color = color,
          Intensity = intensity,
          MolarMass = molarMass
        });
      }
    }
  }
}
