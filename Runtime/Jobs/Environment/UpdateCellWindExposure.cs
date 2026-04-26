using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

using SolarWeb.Pneuma.MathA;

namespace SolarWeb.Pneuma.Jobs.Environment
{
  [BurstCompile]
  public struct UpdateCellWindExposure : IJobParallelFor
  {
    [ReadOnly] public NativeArray<byte> BlocksWind;
    [ReadOnly] public NativeArray<byte> IsRoofed;
    [WriteOnly] public NativeArray<float> CellWindExposure;
    public int MapWidth;
    public int MapHeight;

    public void Execute(int i)
    {
      CellWindExposure[i] = Wind.ComputeExposure(i, MapWidth, MapHeight, BlocksWind, IsRoofed);
    }
  }
}
