using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

using SolarWeb.Pneuma.Constants;

namespace SolarWeb.Pneuma.Jobs.Thermal
{
  [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
  public struct ComputeFaceThermalFlux : IJobParallelFor
  {
    [ReadOnly] public NativeArray<float> TemperatureK;
    [ReadOnly] public NativeArray<int> FaceRegionA, FaceRegionB;
    [ReadOnly] public NativeArray<float> FaceThermalConductivity;
    [ReadOnly] public NativeArray<float> RegionPressureKpa;

    public float TimeStep;
    public int SentinelRegionIndex;

    [WriteOnly] public NativeArray<float> FaceThermalFlux;

    public void Execute(int fIdx)
    {
      int iA = FaceRegionA[fIdx];
      int iB = FaceRegionB[fIdx];

      if (iA == SentinelRegionIndex && iB == SentinelRegionIndex)
      {
        FaceThermalFlux[fIdx] = 0f;
        return;
      }

      float tA = TemperatureK[iA];
      float tB = TemperatureK[iB];

      // Conduction only (Fourier's Law). Advective heat is applied separately by
      // ApplyRegionAdvectiveThermal using the pre-tick PreviousTemperatureK snapshot.
      float avgPressure = (RegionPressureKpa[iA] + RegionPressureKpa[iB]) * 0.5f;
      // Efficiency represents gas-mediated conduction. We maintain a floor of 0.01 (1%) 
      // to represent structural conduction through the physical medium (walls, floors).
      float efficiency = math.clamp(avgPressure / AtmosphereConstants.StandardAtmosphereKpa, 0.01f, 1.0f);
      FaceThermalFlux[fIdx] = FaceThermalConductivity[fIdx] * (tA - tB) * TimeStep * efficiency;
    }
  }
}