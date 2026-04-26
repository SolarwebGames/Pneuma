using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace SolarWeb.Pneuma.Jobs.Diffusion
{
  /// <summary>
  /// Parallel job that sums up gas flux across all gases for each face.
  /// Result is stored in FaceTotalGasFlux, used by ApplyRegionDiffusionFlux
  /// to efficiently calculate liquid entrainment.
  /// </summary>
  [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
  public struct SumFaceTotalFlux : IJobParallelFor
  {
    [ReadOnly] public NativeArray<float> FaceGasFlux; // layout: g * FaceStride + fIdx
    [WriteOnly] public NativeArray<float> FaceTotalGasFlux;

    public int GasCount;
    public int FaceStride;

    public void Execute(int fIdx)
    {
      float total = 0f;
      for (int g = 0; g < GasCount; g++)
      {
        total += FaceGasFlux[g * FaceStride + fIdx];
      }
      FaceTotalGasFlux[fIdx] = total;
    }
  }
}
