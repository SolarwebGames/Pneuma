using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace SolarWeb.Pneuma.Jobs.Thermal
{
  [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
  public struct ComputeStructuralFaceThermalFlux : IJobParallelFor
  {
    [ReadOnly] public NativeArray<float> TemperatureK;
    [ReadOnly] public NativeArray<float> StructuralTemperatureK;
    [ReadOnly] public NativeArray<float> StructuralThermalCapacity;
    [ReadOnly] public NativeArray<int> FaceRegionA, FaceRegionB;
    [ReadOnly] public NativeArray<float> FaceThermalConductivity;

    public float TimeStep;
    public int SentinelRegionIndex;

    [WriteOnly] public NativeArray<float> StructuralFaceThermalFlux;

    public void Execute(int fIdx)
    {
      int iA = FaceRegionA[fIdx];
      int iB = FaceRegionB[fIdx];

      if (iA < 0 || iB < 0 || (iA == SentinelRegionIndex && iB == SentinelRegionIndex))
      {
        StructuralFaceThermalFlux[fIdx] = 0f;
        return;
      }

      float tA = StructuralTemperatureK[iA];
      float tB = StructuralTemperatureK[iB];

      // Fallback to gas temperature for sentinel if its structural temperature wasn't initialized
      if (iA == SentinelRegionIndex && tA <= 0f) tA = TemperatureK[iA];
      if (iB == SentinelRegionIndex && tB <= 0f) tB = TemperatureK[iB];

      float capA = 0f;
      float capB = 0f;

      if (iA < StructuralThermalCapacity.Length) capA = (iA == SentinelRegionIndex) ? float.MaxValue : StructuralThermalCapacity[iA];
      if (iB < StructuralThermalCapacity.Length) capB = (iB == SentinelRegionIndex) ? float.MaxValue : StructuralThermalCapacity[iB];

      // Prevent infinite exchange if either has near-zero capacity
      if (capA <= 1e-6f || capB <= 1e-6f)
      {
        StructuralFaceThermalFlux[fIdx] = 0f;
        return;
      }

      const float StructuralConductionFraction = 0.2f;
      float dT = tA - tB;
      float conductivity = FaceThermalConductivity[fIdx];

      if (conductivity <= 0f)
      {
        StructuralFaceThermalFlux[fIdx] = 0f;
        return;
      }

      float flux = conductivity * dT * StructuralConductionFraction * TimeStep;

      // Clamp to max possible exchange to reach equilibrium
      // If one side is infinite capacity (sentinel), maxFlux = dT * capSide
      float maxFlux;
      if (capA > 1e12f) maxFlux = dT * capB;
      else if (capB > 1e12f) maxFlux = dT * capA;
      else maxFlux = dT * (capA * capB) / (capA + capB);

      if (math.abs(flux) > math.abs(maxFlux))
      {
        flux = maxFlux;
      }

      StructuralFaceThermalFlux[fIdx] = flux;
    }
  }
}