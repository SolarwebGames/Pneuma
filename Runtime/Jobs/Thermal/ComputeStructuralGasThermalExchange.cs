using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace SolarWeb.Pneuma.Jobs.Thermal
{
  [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
  public struct ComputeStructureAndMassGasThermalExchange : IJobParallelForDefer
  {
    [ReadOnly] public NativeArray<float> EnclosingTemperatureK;
    [ReadOnly] public NativeArray<float> InternalMassTemperatureK;
    [ReadOnly] public NativeArray<float> TemperatureK;
    [ReadOnly] public NativeArray<float> EnclosingThermalConductance;
    [ReadOnly] public NativeArray<float> InternalMassThermalConductance;

    public float TimeStep;
    [ReadOnly] public NativeArray<int> ActiveRegionIndices;
    public int SentinelIndex;

    [WriteOnly] public NativeArray<float> EnclosingGasHeatFlux;
    [WriteOnly] public NativeArray<float> InternalMassGasHeatFlux;

    public void Execute(int index)
    {
      int rIdx = ActiveRegionIndices[index];
      if (rIdx < 0 || rIdx == SentinelIndex)
      {
        if (rIdx >= 0)
        {
          EnclosingGasHeatFlux[rIdx] = 0f;
          InternalMassGasHeatFlux[rIdx] = 0f;
        }
        return;
      }

      float eConductance = EnclosingThermalConductance[rIdx];
      float eDT = EnclosingTemperatureK[rIdx] - TemperatureK[rIdx];
      EnclosingGasHeatFlux[rIdx] = eConductance * eDT * TimeStep;

      float iConductance = InternalMassThermalConductance[rIdx];
      float iDT = InternalMassTemperatureK[rIdx] - TemperatureK[rIdx];
      InternalMassGasHeatFlux[rIdx] = iConductance * iDT * TimeStep;
    }
  }
}