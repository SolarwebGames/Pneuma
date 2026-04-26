using Unity.Mathematics;
using Unity.Burst;

namespace SolarWeb.Pneuma.MathA
{
  [BurstCompile]
  public static class ThermalMath
  {
    /// <summary>
    /// Calculates the heat capacity of a region based on gas mixture and vacuum floor,
    /// then applies energy flux (Joules) to update the temperature.
    /// Used by both static (ApplyRegionThermalFlux) and dynamic (ApplyDynamicThermalFlux) jobs.
    /// </summary>
    public static void ApplyHeat(ref float temperatureK, float totalUMoles, float volume, float mixtureMolarCp, float joules)
    {
      float moles = totalUMoles / 1_000_000f;
      float airCapacity = mixtureMolarCp * moles;

      // Prevents division-by-zero or numerical instability in regions with near-zero gas density.
      float vacuumCapacity = (volume / 2.5f) * 30f;
      
      float capacity = (airCapacity + vacuumCapacity) * AtmosphereCalc.GetTemperatureCapacityFactor(temperatureK);

      if (capacity > 1e-6f)
      {
        float deltaT = joules / capacity;
        temperatureK = math.max(1f, temperatureK + deltaT);
      }
    }

    /// <summary>
    /// Calculates the adiabatic compression work (Joules) performed by a pump.
    /// </summary>
    public static float CalculatePumpWorkJ(float pDest, float pSrc, float tSrc, float inflowUMoles, float pumpRate, float timeStep, float maxPumpPressure)
    {
      if (pumpRate <= 0f || inflowUMoles <= 1e-9f) return 0f;

      float deltaP = pDest - pSrc;

      if (maxPumpPressure > 0f)
      {
        float backPressure = math.max(0f, deltaP);
        float pumpScale = math.max(0f, 1f - backPressure / maxPumpPressure);
        pumpRate *= pumpScale;
      }

      // Small offsets prevent log(1) being zero for minimum work and avoid log(infinity) if source pressure is zero.
      float pRatio = math.max(1.05f, pDest / math.max(0.1f, pSrc));
      float workPerMole = 8.314f * tSrc * math.log(pRatio);

      float actualPumpUMoles = math.min(pumpRate * timeStep, inflowUMoles);
      return actualPumpUMoles * 1e-6f * workPerMole;
    }

    /// <summary>
    /// Calculates the advective heat flux (Joules) from gas mass transport.
    /// </summary>
    public static float CalculateAdvectiveJoules(float inflowHeatCapFlux, float tNeighbor, float tSelf)
    {
      if (inflowHeatCapFlux <= 0f) return 0f;
      return inflowHeatCapFlux * 1e-6f * (tNeighbor - tSelf);
    }
  }
}
