using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace SolarWeb.Pneuma.Jobs.UI
{
  [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
  public struct UpdateDangerTexture : IJobParallelFor
  {
    [ReadOnly] public NativeArray<int> WorldToSimIndex;
    [ReadOnly] public NativeArray<float> CellDangerLevels; // batchIdx * Stride + simIdx
    [ReadOnly] public NativeArray<Color32> ColorLookupTable;
    public NativeArray<Color32> OutPixels;

    public int SelectedBatchIdx;
    public int Stride;
    public int SentinelCellIndex;

    public void Execute(int i)
    {
      if (SelectedBatchIdx < 0 || i >= WorldToSimIndex.Length)
      {
        OutPixels[i] = new Color32(0, 0, 0, 0);
        return;
      }

      int simIdx = WorldToSimIndex[i];

      // Use the sentinel danger for any cell mapped to the sentinel (unmapped areas/outside)
      int effectiveSimIdx = (simIdx >= 0 && simIdx < Stride) ? simIdx : SentinelCellIndex;

      if (effectiveSimIdx < 0 || effectiveSimIdx >= Stride)
      {
        OutPixels[i] = new Color32(0, 0, 0, 0);
        return;
      }

      // Danger level is 0.0 to 1.0
      int bufferIdx = SelectedBatchIdx * Stride + effectiveSimIdx;
      if (bufferIdx < 0 || bufferIdx >= CellDangerLevels.Length)
      {
        OutPixels[i] = new Color32(0, 0, 0, 0);
        return;
      }

      float danger = CellDangerLevels[bufferIdx];

      // Map danger to LUT index (0 to 255)
      int lutIndex = (int)math.round(math.saturate(danger) * (ColorLookupTable.Length - 1));
      Color32 color = ColorLookupTable[lutIndex];

      // Use 0.4 alpha (102) if there's any danger, otherwise fully transparent
      color.a = (byte)math.select(0, 102, danger > 0.01f);

      OutPixels[i] = color;
    }
  }
}
