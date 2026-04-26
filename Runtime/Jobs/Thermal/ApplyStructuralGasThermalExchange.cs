using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

using SolarWeb.Pneuma.MathA;

namespace SolarWeb.Pneuma.Jobs.Thermal
{
  [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
  public struct ApplyStructuralGasThermalExchange : IJobParallelForDefer
  {
    [ReadOnly] public NativeArray<float> StructuralGasHeatFlux;
    [ReadOnly] public NativeArray<float> MixtureMolarCp;
    [ReadOnly] public NativeArray<long> TotalUMoles;
    [ReadOnly] public NativeArray<float> StructuralThermalCapacity;
    [ReadOnly] public NativeArray<float> RegionVolumes;

    public NativeArray<float> TemperatureK;
    public NativeArray<float> StructuralTemperatureK;

    [ReadOnly] public NativeArray<int> ActiveRegionIndices;
    public int SentinelIndex;

    public void Execute(int index)
    {
      int rIdx = ActiveRegionIndices[index];
      if (rIdx < 0 || rIdx == SentinelIndex) return;

      float flux = StructuralGasHeatFlux[rIdx];
      if (math.abs(flux) < 1e-10f) return;

      // Apply to gas (air + vacuum capacity only — structCap excluded)
      float moles = TotalUMoles[rIdx] / 1_000_000f;
      float vacuumCap = (RegionVolumes[rIdx] / 2.5f) * 30f;
      float gasCap = (MixtureMolarCp[rIdx] * moles + vacuumCap)
                      * AtmosphereCalc.GetTemperatureCapacityFactor(TemperatureK[rIdx]);

      float structCap = StructuralThermalCapacity[rIdx];

      // Bail if either has no capacity to prevent infinite energy transfer or division by zero
      if (gasCap <= 1e-6f || structCap <= 1e-6f) return;

      // Clamp flux to the maximum possible exchange to reach equilibrium
      float dT = StructuralTemperatureK[rIdx] - TemperatureK[rIdx];
      float maxFlux = dT * (gasCap * structCap) / (gasCap + structCap);

      if (math.abs(flux) > math.abs(maxFlux))
      {
        flux = maxFlux;
      }

      // Calculate new structure temperature and apply
      float oldStructTemp = StructuralTemperatureK[rIdx];
      float structDeltaT = -flux / structCap;
      float newStructTemp = math.max(1f, oldStructTemp + structDeltaT);
      StructuralTemperatureK[rIdx] = newStructTemp;

      // Determine the actual energy transferred after float precision limits
      // This prevents the structure from acting as an infinite heat source
      float actualStructDeltaT = newStructTemp - oldStructTemp;
      float actualFlux = -actualStructDeltaT * structCap;

      // Apply actual flux to gas
      float gasDeltaT = actualFlux / gasCap;
      TemperatureK[rIdx] = math.max(1f, TemperatureK[rIdx] + gasDeltaT);
    }
  }
}