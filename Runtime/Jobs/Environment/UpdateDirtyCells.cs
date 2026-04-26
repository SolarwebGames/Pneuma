using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

using SolarWeb.Pneuma.MathA;

namespace SolarWeb.Pneuma.Jobs.Environment
{
  [BurstCompile]
  public struct UpdateDirtyCells : IJobParallelFor
  {
    [ReadOnly] public NativeArray<int> DirtyIndices;
    [ReadOnly] public NativeArray<byte> BlocksWind;
    [ReadOnly] public NativeArray<byte> IsRoofed;
    [NativeDisableParallelForRestriction] public NativeArray<float> CellWindExposure;
    [NativeDisableParallelForRestriction] public NativeArray<byte> IsCellDirty;
    public int MapWidth;
    public int MapHeight;

    public void Execute(int idx)
    {
      int i = DirtyIndices[idx];
      CellWindExposure[i] = Wind.ComputeExposure(i, MapWidth, MapHeight, BlocksWind, IsRoofed);
      IsCellDirty[i] = 0;
    }
  }
}
