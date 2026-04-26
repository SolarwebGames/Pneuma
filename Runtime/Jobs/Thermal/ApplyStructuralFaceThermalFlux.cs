using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace SolarWeb.Pneuma.Jobs.Thermal
{
  [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
  public struct ApplyStructuralFaceThermalFlux : IJobParallelForDefer
  {
    [ReadOnly] public NativeArray<int> RegionFaceOffsets, RegionFaceCounts, RegionFaceIndices;
    [ReadOnly] public NativeArray<int> FaceRegionA;

    [ReadOnly] public NativeArray<float> StructuralFaceThermalFlux;
    [ReadOnly] public NativeArray<float> StructuralThermalCapacity;

    public NativeArray<float> StructuralTemperatureK;
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
        totalJoulesNet += StructuralFaceThermalFlux[fIdx] * direction;
      }

      float structCapacity = StructuralThermalCapacity[simIdx];

      if (structCapacity > 1e-6f)
      {
        float deltaT = totalJoulesNet / structCapacity;
        StructuralTemperatureK[simIdx] = math.max(1f, StructuralTemperatureK[simIdx] + deltaT);
      }
    }
  }
}