using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

using SolarWeb.Pneuma.MathA;

namespace SolarWeb.Pneuma.Jobs.Thermal
{
  [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
  public struct ApplyStructureAndMassGasThermalExchange : IJobParallelForDefer
  {
    [ReadOnly] public NativeArray<float> EnclosingGasHeatFlux;
    [ReadOnly] public NativeArray<float> InternalMassGasHeatFlux;
    [ReadOnly] public NativeArray<float> MixtureMolarCp;
    [ReadOnly] public NativeArray<long> TotalUMoles;
    [ReadOnly] public NativeArray<float> EnclosingThermalCapacity;
    [ReadOnly] public NativeArray<float> InternalMassThermalCapacity;
    [ReadOnly] public NativeArray<float> RegionVolumes;

    public NativeArray<float> TemperatureK;
    public NativeArray<float> EnclosingTemperatureK;
    public NativeArray<float> InternalMassTemperatureK;

    [ReadOnly] public NativeArray<int> ActiveRegionIndices;
    public int SentinelIndex;

    public void Execute(int index)
    {
      int rIdx = ActiveRegionIndices[index];
      if (rIdx < 0 || rIdx == SentinelIndex) return;

      float eFlux = EnclosingGasHeatFlux[rIdx];
      float iFlux = InternalMassGasHeatFlux[rIdx];
      if (math.abs(eFlux) < 1e-10f && math.abs(iFlux) < 1e-10f) return;

      // Calculate gas capacity
      float moles = TotalUMoles[rIdx] / 1_000_000f;
      float vacuumCap = (RegionVolumes[rIdx] / 2.5f) * 30f;
      float gasCap = (MixtureMolarCp[rIdx] * moles + vacuumCap)
                      * AtmosphereCalc.GetTemperatureCapacityFactor(TemperatureK[rIdx]);

      if (gasCap <= 1e-6f) return;

      float totalActualFlux = 0f;
      float currentGasTemp = TemperatureK[rIdx];

      // 1. Process Enclosing Structure
      float eCap = EnclosingThermalCapacity[rIdx];
      if (eCap > 1e-6f && math.abs(eFlux) > 1e-10f)
      {
        float dT = EnclosingTemperatureK[rIdx] - currentGasTemp;
        float maxFlux = dT * (gasCap * eCap) / (gasCap + eCap);
        if (math.abs(eFlux) > math.abs(maxFlux)) eFlux = maxFlux;

        float oldTemp = EnclosingTemperatureK[rIdx];
        float newTemp = math.max(1f, oldTemp - eFlux / eCap);
        EnclosingTemperatureK[rIdx] = newTemp;
        
        float actualFlux = -(newTemp - oldTemp) * eCap;
        totalActualFlux += actualFlux;
        currentGasTemp += actualFlux / gasCap;
      }

      // 2. Process Internal Mass
      float iCap = InternalMassThermalCapacity[rIdx];
      if (iCap > 1e-6f && math.abs(iFlux) > 1e-10f)
      {
        float dT = InternalMassTemperatureK[rIdx] - currentGasTemp;
        float maxFlux = dT * (gasCap * iCap) / (gasCap + iCap);
        if (math.abs(iFlux) > math.abs(maxFlux)) iFlux = maxFlux;

        float oldTemp = InternalMassTemperatureK[rIdx];
        float newTemp = math.max(1f, oldTemp - iFlux / iCap);
        InternalMassTemperatureK[rIdx] = newTemp;

        float actualFlux = -(newTemp - oldTemp) * iCap;
        totalActualFlux += actualFlux;
      }

      // 3. Final Gas Temperature Update
      TemperatureK[rIdx] = math.max(1f, TemperatureK[rIdx] + totalActualFlux / gasCap);
    }
  }
}