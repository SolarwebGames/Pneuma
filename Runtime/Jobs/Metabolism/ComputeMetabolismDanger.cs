using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

using SolarWeb.Pneuma.Metabolism;

namespace SolarWeb.Pneuma.Jobs.Metabolism
{
  [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
  public struct ComputeMetabolismDanger : IJobParallelForDefer
  {
    // --- Inputs: Region State ---
    [ReadOnly] public NativeArray<int> ActiveRegionIndices;
    [ReadOnly] public NativeArray<long> uMoles;          // gasId * RegionStride + regIdx
    [ReadOnly] public NativeArray<long> TotalUMoles;     // regIdx
    [ReadOnly] public NativeArray<float> PressureKpa;    // regIdx
    [ReadOnly] public NativeArray<float> TemperatureK;   // regIdx

    // --- Inputs: Gas Properties ---
    [ReadOnly] public NativeArray<float> LowerExplosiveLimit;
    [ReadOnly] public NativeArray<float> UpperExplosiveLimit;
    [ReadOnly] public NativeArray<float> OxidizingPotency;
    [ReadOnly] public NativeArray<float> Radioactivity;
    [ReadOnly] public NativeArray<float> Corrosiveness;
    [ReadOnly] public NativeArray<float> MolarHeatCapacityCp;
    public ulong FuelGasMask;

    // --- Inputs: Metabolism Specs ---
    [ReadOnly] public NativeArray<MetabolismDangerCriteria> BatchDangerSpecs;
    [ReadOnly] public NativeArray<GasDangerThreshold> BatchGasDangerMatrix; // batchIdx * GasCount + gasId
    [ReadOnly] public NativeArray<ulong> BatchRelevantGasMasks;
    [ReadOnly] public NativeArray<ulong> BatchRequiredGasMasks;

    // --- Outputs ---
    [NativeDisableParallelForRestriction]
    public NativeArray<float> DangerLevels; // batchIdx * RegionStride + regIdx

    [NativeDisableParallelForRestriction]
    public NativeArray<float> PhysicalHazardLevels; // hazardIdx * RegionStride + regIdx

    public int BatchCount;
    public int GasCount;
    public int RegionStride;
    public int SentinelRegionIndex;
    public bool UpdateBiological;

    public void Execute(int index)
    {
      int rIdx = ActiveRegionIndices[index];

      // Safety: Ignore sentinel region if it shouldn't be processed by the biological scanner 
      // (though user wants to see its danger, so we allow it but ensure uMoles/TotalUMoles are synced).

      float pressure = PressureKpa[rIdx];
      float temperature = TemperatureK[rIdx];

      // optimization: Load molar fractions into stack and build present-gas mask simultaneously
      unsafe
      {
        float* localFracs = stackalloc float[GasCount];
        ulong presentMask = 0;

        float invTotal = 1.0f / math.max(1.0f, (float)TotalUMoles[rIdx]);

        for (int g = 0; g < GasCount; g++)
        {
          float frac = uMoles[g * RegionStride + rIdx] * invTotal;
          localFracs[g] = frac;
          if (frac > 0.0001f) presentMask |= 1UL << g; // Use a small noise floor for the mask
        }

        // 1. Calculate Per-Region Physical Hazards
        float totalOxidizingPotency = 0;
        float totalRadioactivity = 0;
        float totalCorrosiveness = 0;
        float totalMixtureCp = 0;
        float maxExplosionRisk = 0;

        // Scan only present gases for physical hazards
        ulong hazardScanMask = presentMask;
        while (hazardScanMask != 0)
        {
          int g = math.tzcnt(hazardScanMask);
          float frac = localFracs[g];

          totalOxidizingPotency += frac * OxidizingPotency[g];
          totalRadioactivity += frac * Radioactivity[g];
          totalCorrosiveness += frac * Corrosiveness[g];
          totalMixtureCp += frac * MolarHeatCapacityCp[g];

          // Explosion Risk: Is this gas a fuel and within LEL/UEL?
          if ((FuelGasMask & (1UL << g)) != 0)
          {
            float fuelPct = frac * 100f;
            float lel = LowerExplosiveLimit[g];
            float uel = UpperExplosiveLimit[g];

            if (fuelPct >= lel && fuelPct <= uel)
            {
              maxExplosionRisk = math.max(maxExplosionRisk, 1.0f);
            }
            else if (fuelPct > 0)
            {
              float nearLEL = math.saturate(1.0f - math.abs(fuelPct - lel) / 5.0f);
              maxExplosionRisk = math.max(maxExplosionRisk, nearLEL * 0.5f);
            }
          }
          hazardScanMask &= ~(1UL << g);
        }

        // Explosion needs oxidizer.
        maxExplosionRisk *= math.saturate(totalOxidizingPotency / 0.1f);
        float causticHazard = math.saturate(totalCorrosiveness / 1.0f); // 1.0 stress = max hazard

        PhysicalHazardLevels[0 * RegionStride + rIdx] = maxExplosionRisk;
        PhysicalHazardLevels[1 * RegionStride + rIdx] = math.saturate(totalRadioactivity * 10.0f);
        PhysicalHazardLevels[2 * RegionStride + rIdx] = causticHazard;
        PhysicalHazardLevels[3 * RegionStride + rIdx] = 0; // Thermal hazard disabled

        // 2. Calculate Metabolism-Specific Danger Levels
        if (UpdateBiological)
        {
          for (int b = 0; b < BatchCount; b++)
          {
            var criteria = BatchDangerSpecs[b];
            float danger = 0;

            // A. (Removed) Temperature Danger

            // B. Pressure Danger
            if (pressure < (criteria.MinPressure - 1.0f))
            {
              float range = criteria.MinPressure - criteria.MinPressureFullDanger;
              danger = math.max(danger, math.saturate((criteria.MinPressure - pressure) / math.max(1.0f, range)));
            }
            else if (pressure > (criteria.MaxPressure + 1.0f))
            {
              float range = criteria.MaxPressureFullDanger - criteria.MaxPressure;
              danger = math.max(danger, math.saturate((pressure - criteria.MaxPressure) / math.max(1.0f, range)));
            }

            // C. Generalized Gas Danger Scan (Sparse)
            // Check gases that are either present or required/interesting for this batch
            ulong checkMask = BatchRelevantGasMasks[b] & (presentMask | BatchRequiredGasMasks[b]);
            int matrixBase = b * GasCount;

            while (checkMask != 0)
            {
              int g = math.tzcnt(checkMask);
              var threshold = BatchGasDangerMatrix[matrixBase + g];
              float currentFrac = localFracs[g];

              if (currentFrac < threshold.MinFraction)
              {
                float range = threshold.MinFraction - threshold.MinFullDangerFraction;
                danger = math.max(danger, math.saturate((threshold.MinFraction - currentFrac) / math.max(0.0001f, range)));
              }
              else if (currentFrac > threshold.MaxFraction)
              {
                float range = threshold.MaxFullDangerFraction - threshold.MaxFraction;
                danger = math.max(danger, math.saturate((currentFrac - threshold.MaxFraction) / math.max(0.0001f, range)));
              }
              checkMask &= ~(1UL << g);
            }

            // D. Biological vulnerability to environmental hazards
            float effectiveRad = totalRadioactivity * (1.0f - criteria.RadiationResistance) * criteria.RadiationSensitivity;
            danger = math.max(danger, effectiveRad);

            float effectiveCaustic = causticHazard * (1.0f - criteria.CausticResistance) * criteria.CausticSensitivity;
            danger = math.max(danger, effectiveCaustic);

            // Thermal hazard disabled

            DangerLevels[b * RegionStride + rIdx] = danger;
          }
        }
      }
    }
  }
}
