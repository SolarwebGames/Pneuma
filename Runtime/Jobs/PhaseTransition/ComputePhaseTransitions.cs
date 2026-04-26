using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

using SolarWeb.Pneuma.MathA;
using SolarWeb.Pneuma.Logging;

namespace SolarWeb.Pneuma.Jobs.PhaseTransition
{
  /// <summary>
  /// Computes evaporation and condensation for all condensable gases in each active region.
  ///
  /// Physics: Antoine equation — log₁₀(P_sat_bar) = A − B / (C + T_K)
  ///   P_sat converted to kPa: P_sat_kPa = 10^(log10Psat) × 100
  ///
  /// Condensation (P_actual > P_sat): excess gas molecules transfer to liquid storage.
  /// Evaporation  (P_actual < P_sat, liquid > 0): liquid molecules transfer to gas phase.
  ///
  /// Rate is limited by CondensationRate / EvaporationRate [s⁻¹] so the system
  /// approaches equilibrium with a ~1/rate second time constant.
  ///
  /// Safe to run in parallel: each Execute() index maps to a unique region via
  /// ActiveRegionIndices, so no two invocations touch the same memory locations.
  /// </summary>
  [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
  public struct ComputePhaseTransitions : IJobParallelForDefer
  {
    [ReadOnly] public NativeArray<int> ActiveRegionIndices;
    [ReadOnly] public NativeArray<long> TotalUMoles;        // [regionIdx]
    [ReadOnly] public NativeArray<float> PressureKpa;        // [regionIdx]
    public NativeArray<float> TemperatureK;                  // [regionIdx] - Latent heat applied here

    // Per-gas properties
    [ReadOnly] public NativeArray<float> AntoineA;
    [ReadOnly] public NativeArray<float> AntoineB;
    [ReadOnly] public NativeArray<float> AntoineC;
    [ReadOnly] public NativeArray<float> MixtureMolarCp; // [regionIdx]
    [ReadOnly] public NativeArray<float> RegionVolumes; // [regionIdx]

    // Both read and written; each Execute() touches unique indices (g * RegionStride + simIdx)
    public NativeArray<long> GasUMoles;      // [gasId * RegionStride + regionIdx]
    public NativeArray<long> LiquidUMoles;   // [gasId * RegionStride + regionIdx]

    public int GasCount;
    public int RegionStride;
    public int SentinelIndex;
    public float TimeStep;
    public float EvaporationRate;    // [s⁻¹] — rate constant for liquid→gas
    public float CondensationRate;   // [s⁻¹] — rate constant for gas→liquid

    private const float R_Constant = 8.314462618f;
    private const float Ln10 = 2.302585093f;

    public void Execute(int index)
    {
      int simIdx = ActiveRegionIndices[index];
      if (simIdx == SentinelIndex) return;

      long totalUMol = TotalUMoles[simIdx];
      if (totalUMol <= 0) return;

      float invTotal = 1.0f / (float)totalUMol;
      float pressureKpa = PressureKpa[simIdx];
      float tempK = TemperatureK[simIdx];

      // Calculate current total thermal capacity of the region (Gases)
      float moles = totalUMol / 1_000_000f;
      float vacuumCapacity = (RegionVolumes[simIdx] / 2.5f) * 30f;
      float totalThermalCapacity = (MixtureMolarCp[simIdx] * moles + vacuumCapacity) * AtmosphereCalc.GetTemperatureCapacityFactor(tempK);

      if (totalThermalCapacity <= 0) return;

      float netEnergyChangeJ = 0f;

      for (int g = 0; g < GasCount; g++)
      {
        float A = AntoineA[g];
        if (A == 0f) continue; // not condensable

        float B = AntoineB[g];
        float C = AntoineC[g];

        // Saturation vapor pressure via Antoine equation
        float tClamped = math.max(tempK, 1f);
        float log10Psat = A - B / (C + tClamped);
        float pSatKpa = math.pow(10f, log10Psat) * 100f; // bar → kPa

        int dataIdx = (g * RegionStride) + simIdx;
        long gasUMol = GasUMoles[dataIdx];
        long liquidMol = LiquidUMoles[dataIdx];

        // Current partial pressure of this gas
        float x = (float)gasUMol * invTotal;
        float pActual = x * pressureKpa;

        float dP = pActual - pSatKpa;
        long transfer = 0;

        if (dP > 0f)
        {
          // Supersaturated → condense the excess
          long excess = (long)((dP / math.max(pressureKpa, 0.001f)) * (float)totalUMol);
          transfer = math.min(excess, (long)((float)gasUMol * CondensationRate * TimeStep));
          transfer = math.clamp(transfer, 0L, gasUMol);

          GasUMoles[dataIdx] = gasUMol - transfer;
          LiquidUMoles[dataIdx] = liquidMol + transfer;

          // Condensation releases latent heat (Exothermic)
          // DeltaH_vap approx B * R * ln(10)
          float hVap = B * R_Constant * Ln10;
          netEnergyChangeJ += (transfer * 1e-6f) * hVap;
        }
        else if (dP < 0f && liquidMol > 0)
        {
          // Undersaturated + liquid present → evaporate
          long deficit = (long)((-dP / math.max(pressureKpa, 0.001f)) * (float)totalUMol);
          transfer = math.min(deficit, (long)((float)liquidMol * EvaporationRate * TimeStep));
          transfer = math.clamp(transfer, 0L, liquidMol);

          GasUMoles[dataIdx] = gasUMol + transfer;
          LiquidUMoles[dataIdx] = liquidMol - transfer;

          // Evaporation absorbs latent heat (Endothermic)
          float hVap = B * R_Constant * Ln10;
          netEnergyChangeJ -= (transfer * 1e-6f) * hVap;
        }
      }

      if (math.abs(netEnergyChangeJ) > 1e-6f)
      {
        TemperatureK[simIdx] = tempK + (netEnergyChangeJ / totalThermalCapacity);
      }
    }
  }
}
