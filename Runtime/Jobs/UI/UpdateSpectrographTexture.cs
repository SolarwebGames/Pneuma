using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace SolarWeb.Pneuma.Jobs.UI
{
  [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
  public struct UpdateSpectrographTexture : IJobParallelFor
  {
    [ReadOnly] public NativeArray<int> WorldToRegionIndex;
    [ReadOnly] public NativeArray<float> MolarFractions; // gasId * Stride + regIdx
    [ReadOnly] public NativeArray<Color32> ColorLookupTable;
    public NativeArray<Color32> OutPixels;

    public int SelectedGasId;
    public int Stride;
    public int SentinelRegionIndex;

    public void Execute(int i)
    {
      int regIdx = WorldToRegionIndex[i];

      if (regIdx == SentinelRegionIndex || SelectedGasId < 0)
      {
        OutPixels[i] = new Color32(0, 0, 0, 0);
        return;
      }

      // Molar fraction is 0.0 to 1.0
      float fraction = MolarFractions[SelectedGasId * Stride + regIdx];

      // Map fraction to LUT index (0 to 255)
      int lutIndex = (int)math.round(math.saturate(fraction) * (ColorLookupTable.Length - 1));
      Color32 color = ColorLookupTable[lutIndex];

      // Use 0.5 alpha (127) for visibility
      color.a = (byte)math.select(0, 127, fraction > 0.001f);

      OutPixels[i] = color;
    }
  }
}
