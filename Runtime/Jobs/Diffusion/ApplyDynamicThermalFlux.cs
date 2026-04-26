using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

using SolarWeb.Pneuma.MathA;

namespace SolarWeb.Pneuma.Jobs.Diffusion
{
  [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
  public struct ApplyDynamicThermalFlux : IJob
  {
    [ReadOnly] public NativeArray<int> FaceRegionA;
    [ReadOnly] public NativeArray<int> FaceRegionB;
    [ReadOnly] public NativeArray<float> FaceThermalFlux;

    [ReadOnly] public NativeArray<float> MixtureMolarCp;
    [ReadOnly] public NativeArray<long> TotalUMoles;
    [ReadOnly] public NativeArray<float> RegionVolumes;

    public NativeArray<float> TemperatureK;

    public int FaceCount;
    public int SentinelRegionIndex;

    public void Execute()
    {
      for (int fIdx = 0; fIdx < FaceCount; fIdx++)
      {
        int iA = FaceRegionA[fIdx];
        int iB = FaceRegionB[fIdx];

        if (iA == SentinelRegionIndex && iB == SentinelRegionIndex)
          continue;

        float flux = FaceThermalFlux[fIdx];
        if (flux == 0f) continue;

        bool aIsSentinel = (iA == SentinelRegionIndex);
        bool bIsSentinel = (iB == SentinelRegionIndex);

        if (!aIsSentinel)
        {
          float temp = TemperatureK[iA];
          ThermalMath.ApplyHeat(ref temp, (float)TotalUMoles[iA], RegionVolumes[iA], MixtureMolarCp[iA], -flux);
          TemperatureK[iA] = temp;
        }

        if (!bIsSentinel)
        {
          float temp = TemperatureK[iB];
          ThermalMath.ApplyHeat(ref temp, (float)TotalUMoles[iB], RegionVolumes[iB], MixtureMolarCp[iB], flux);
          TemperatureK[iB] = temp;
        }
      }
    }
  }
}