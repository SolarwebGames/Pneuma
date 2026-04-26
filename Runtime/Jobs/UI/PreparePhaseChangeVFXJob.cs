using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

using SolarWeb.Pneuma.Data;

namespace SolarWeb.Pneuma.Jobs.UI
{
  [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
  public struct PreparePhaseChangeVFXJob : IJob
  {
    [ReadOnly] public NativeArray<int> ActiveRegionIndices;
    [ReadOnly] public NativeArray<long> SolidUMoles; // Stride * GasCount
    [ReadOnly] public NativeArray<long> PreviousSolidUMoles;
    [ReadOnly] public NativeArray<long> LiquidUMoles;
    [ReadOnly] public NativeArray<long> PreviousLiquidUMoles;
    [ReadOnly] public NativeArray<float3> GasOverlayColor;
    [ReadOnly] public NativeArray<int> RegionMinX;
    [ReadOnly] public NativeArray<int> RegionMaxX;
    [ReadOnly] public NativeArray<int> RegionMinZ;
    [ReadOnly] public NativeArray<int> RegionMaxZ;

    public NativeList<PhaseChangePointData> OutPhaseChangePoints;

    public int ActiveRegionCount;
    public int GasCount;
    public int RegionStride;
    public float VisibilityThresholdMoles;

    public void Execute()
    {
      for (int i = 0; i < ActiveRegionCount; i++)
      {
        int rIdx = ActiveRegionIndices[i];

        for (int g = 0; g < GasCount; g++)
        {
          int gasIdx = g * RegionStride + rIdx;

          long currentSolid = SolidUMoles[gasIdx];
          long prevSolid = PreviousSolidUMoles[gasIdx];
          long deltaSolid = currentSolid - prevSolid;

          long currentLiquid = LiquidUMoles[gasIdx];
          long prevLiquid = PreviousLiquidUMoles[gasIdx];
          long deltaLiquid = currentLiquid - prevLiquid;

          float deltaSolidMoles = deltaSolid / 1e6f;
          float deltaLiquidMoles = deltaLiquid / 1e6f;

          if (deltaSolidMoles > VisibilityThresholdMoles)
          {
            AddPoint(rIdx, g, deltaSolidMoles, true);
          }

          if (deltaLiquidMoles > VisibilityThresholdMoles)
          {
            AddPoint(rIdx, g, deltaLiquidMoles, false);
          }
        }
      }
    }

    private void AddPoint(int rIdx, int gIdx, float deltaMoles, bool isSolid)
    {
      float3 color = GasOverlayColor[gIdx];
      float centerX = (RegionMinX[rIdx] + RegionMaxX[rIdx] + 1) * 0.5f;
      float centerZ = (RegionMinZ[rIdx] + RegionMaxZ[rIdx] + 1) * 0.5f;

      float intensity = math.saturate(deltaMoles / 10f);

      OutPhaseChangePoints.Add(new PhaseChangePointData
      {
        Position = new float3(centerX, 0, centerZ),
        Color = color,
        IsSolid = isSolid,
        Intensity = intensity
      });
    }
  }
}
