using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace SolarWeb.Pneuma.Jobs.Thermal
{
  [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
  public struct ComputeStructuralGasThermalExchange : IJobParallelForDefer
  {
    [ReadOnly] public NativeArray<float> StructuralTemperatureK;
    [ReadOnly] public NativeArray<float> TemperatureK;
    [ReadOnly] public NativeArray<float> StructuralThermalConductance;

    public float TimeStep;
    [ReadOnly] public NativeArray<int> ActiveRegionIndices;
    public int SentinelIndex;

    [WriteOnly] public NativeArray<float> StructuralGasHeatFlux;

    public void Execute(int index)
    {
      int rIdx = ActiveRegionIndices[index];
      if (rIdx < 0 || rIdx == SentinelIndex)
      {
        if (rIdx >= 0) StructuralGasHeatFlux[rIdx] = 0f;
        return;
      }

      float conductance = StructuralThermalConductance[rIdx];
      if (conductance <= 0f)
      {
        StructuralGasHeatFlux[rIdx] = 0f;
        return;
      }

      float dT = StructuralTemperatureK[rIdx] - TemperatureK[rIdx];
      float flux = conductance * dT * TimeStep;
      StructuralGasHeatFlux[rIdx] = flux; // positive = struct→gas
    }
  }
}