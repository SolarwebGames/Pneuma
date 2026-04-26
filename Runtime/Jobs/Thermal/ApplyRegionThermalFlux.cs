using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

using SolarWeb.Pneuma.MathA;

namespace SolarWeb.Pneuma.Jobs.Thermal
{
  [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
  public struct ApplyRegionThermalFlux : IJobParallelForDefer
  {
    [ReadOnly] public NativeArray<int> RegionFaceOffsets, RegionFaceCounts, RegionFaceIndices;
    [ReadOnly] public NativeArray<int> FaceRegionA;

    [ReadOnly] public NativeArray<float> FaceThermalFlux;

    [ReadOnly] public NativeArray<float> MixtureMolarCp;
    [ReadOnly] public NativeArray<long> TotalUMoles;
    [ReadOnly] public NativeArray<float> RegionVolumes;

    public NativeArray<float> TemperatureK;
    [ReadOnly] public NativeArray<int> ActiveRegionIndices;
    public int SentinelIndex;

    public void Execute(int index)
    {
      int simIdx = ActiveRegionIndices[index];
      if (simIdx == SentinelIndex) return;

      int offset = RegionFaceOffsets[simIdx];
      int count = RegionFaceCounts[simIdx];

      float totalJoulesNet = 0;

      for (int i = 0; i < count; i++)
      {
        int fIdx = RegionFaceIndices[offset + i];
        float direction = (FaceRegionA[fIdx] == simIdx) ? -1.0f : 1.0f;
        totalJoulesNet += FaceThermalFlux[fIdx] * direction;
      }

      float temp = TemperatureK[simIdx];
      ThermalMath.ApplyHeat(ref temp, (float)TotalUMoles[simIdx], RegionVolumes[simIdx], MixtureMolarCp[simIdx], totalJoulesNet);
      TemperatureK[simIdx] = temp;
    }
  }
}