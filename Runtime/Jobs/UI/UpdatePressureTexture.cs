using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace SolarWeb.Pneuma.Jobs.UI
{
  [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
  public struct UpdatePressureTexture : IJobParallelFor
  {
    [ReadOnly] public NativeArray<int> WorldToRegionIndex;
    [ReadOnly] public NativeArray<float> RegionPressureKpa;
    [ReadOnly] public NativeArray<Color32> ColorLookupTable;
    public NativeArray<Color32> OutPixels;

    public int DynamicRegionPoolStart;
    public int SentinelRegionIndex;
    public float AtmosphereLow;
    public float MaxPressureRange;

    public void Execute(int i)
    {
      if (i >= WorldToRegionIndex.Length)
      {
        OutPixels[i] = new Color32(0, 0, 0, 0);
        return;
      }

      int regIdx = WorldToRegionIndex[i];

      // Allow rendering for the sentinel region (external environment)
      bool trulyInvalid = regIdx == -1 || regIdx < 0 || regIdx >= RegionPressureKpa.Length;
      int safeRegIdx = math.select(regIdx, SentinelRegionIndex, trulyInvalid);

      // Secondary safety check for the safe index
      if (safeRegIdx < 0 || safeRegIdx >= RegionPressureKpa.Length) safeRegIdx = 0;

      float pressure = RegionPressureKpa[safeRegIdx];

      // Handle NaN or Infinity which can occur during simulation instability
      if (!math.isfinite(pressure))
      {
        pressure = AtmosphereLow;
        trulyInvalid = true;
      }

      float normalized = math.saturate((pressure - AtmosphereLow) / math.max(1f, MaxPressureRange));

      // Map normalized pressure to LUT index (0 to ColorLookupTable.Length - 1)
      int lutMax = ColorLookupTable.Length - 1;
      int lutIndex = (int)math.round(normalized * lutMax);
      lutIndex = math.clamp(lutIndex, 0, math.max(0, lutMax));

      Color32 color = ColorLookupTable[lutIndex];

      // Dynamic opacity logic
      // Use 153 (0.6) for dynamic, 51 (0.2) for static/sentinel, 0 for truly invalid
      byte alpha = (byte)math.select(51, 153, !trulyInvalid && regIdx >= DynamicRegionPoolStart && regIdx < SentinelRegionIndex);
      color.a = (byte)math.select((int)alpha, 0, trulyInvalid);

      OutPixels[i] = color;
    }
  }
}
