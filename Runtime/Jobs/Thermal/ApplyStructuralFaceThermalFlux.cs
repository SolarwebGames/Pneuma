using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace SolarWeb.Pneuma.Jobs.Thermal
{
  [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
  public struct ApplyEnclosingFaceThermalFlux : IJobParallelForDefer
  {
    [ReadOnly] public NativeArray<int> RegionFaceOffsets, RegionFaceCounts, RegionFaceIndices;
    [ReadOnly] public NativeArray<int> FaceRegionA;

    [ReadOnly] public NativeArray<float> EnclosingFaceThermalFlux;
    [ReadOnly] public NativeArray<float> EnclosingThermalCapacity;

    public NativeArray<float> EnclosingTemperatureK;
    [ReadOnly] public NativeArray<int> ActiveRegionIndices;
    public int SentinelIndex;

    public void Execute(int index)
    {
      int simIdx = ActiveRegionIndices[index];
      if (simIdx < 0 || simIdx == SentinelIndex) return;

      int offset = RegionFaceOffsets[simIdx];
      int count = RegionFaceCounts[simIdx];

      float totalJoulesNet = 0;

      for (int i = 0; i < count; i++)
      {
        int fIdx = RegionFaceIndices[offset + i];
        float direction = (FaceRegionA[fIdx] == simIdx) ? -1.0f : 1.0f;
        totalJoulesNet += EnclosingFaceThermalFlux[fIdx] * direction;
      }

      float structCapacity = EnclosingThermalCapacity[simIdx];

      if (structCapacity > 1e-6f)
      {
        float deltaT = totalJoulesNet / structCapacity;
        EnclosingTemperatureK[simIdx] = math.max(1f, EnclosingTemperatureK[simIdx] + deltaT);
      }
    }
  }
}