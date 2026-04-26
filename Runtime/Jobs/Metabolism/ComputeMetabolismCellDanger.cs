using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

using SolarWeb.Pneuma.Metabolism;

namespace SolarWeb.Pneuma.Jobs.Metabolism
{
  [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
  public struct ComputeMetabolismCellDanger : IJobParallelFor
  {
    // --- Inputs: Cell Overlay State ---
    [ReadOnly] public NativeArray<int> ActiveCellIndices;
    [ReadOnly] public NativeArray<float> MolarFractions; // gasId * Stride + simIdx
    [ReadOnly] public NativeArray<float> PressureKpa;    // simIdx
    [ReadOnly] public NativeArray<float> TemperatureK;   // simIdx

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
    public NativeArray<float> CellDangerLevels; // batchIdx * Stride + simIdx

    [NativeDisableParallelForRestriction]
    public NativeArray<float> CellPhysicalHazardLevels; // hazardIdx * Stride + simIdx

    public int BatchCount;
    public int GasCount;
    public int Stride;
    public float ReferenceBodyTempK;
    public bool UpdateBiological;

    public void Execute(int index)
    {
      int simIdx = ActiveCellIndices[index];

      float pressure = PressureKpa[simIdx];
      float temperature = TemperatureK[simIdx];

      unsafe
      {
        float* localFracs = stackalloc float[GasCount];
        ulong presentMask = 0;
        for (int g = 0; g < GasCount; g++)
        {
          float f = MolarFractions[g * Stride + simIdx];
          localFracs[g] = f;
          if (f > 0) presentMask |= (1UL << g);
        }

        // 1. Calculate Per-Cell Physical Hazards
        float totalOxidizingPotency = 0;
        float totalRadioactivity = 0;
        float totalCorrosiveness = 0;
        float totalMixtureCp = 0;
        float maxExplosionRisk = 0;

        ulong hazardScanMask = presentMask;
        while (hazardScanMask != 0)
        {
          int g = math.tzcnt(hazardScanMask);
          float frac = localFracs[g];

          totalOxidizingPotency += frac * OxidizingPotency[g];
          totalRadioactivity += frac * Radioactivity[g];
          totalCorrosiveness += frac * Corrosiveness[g];
          totalMixtureCp += frac * MolarHeatCapacityCp[g];

          if ((FuelGasMask & (1UL << g)) != 0)
          {
            float fuelPct = frac * 100f;
            float lel = LowerExplosiveLimit[g];
            float uel = UpperExplosiveLimit[g];
            if (fuelPct >= lel && fuelPct <= uel) maxExplosionRisk = math.max(maxExplosionRisk, 1.0f);
            else if (fuelPct > 0) maxExplosionRisk = math.max(maxExplosionRisk, math.saturate(1.0f - math.abs(fuelPct - lel) / 5.0f) * 0.5f);
          }
          hazardScanMask &= ~(1UL << g);
        }

        maxExplosionRisk *= math.saturate(totalOxidizingPotency / 0.1f);
        float causticHazard = math.saturate(totalCorrosiveness / 1.0f);
        float thermalDelta = math.max(0, temperature - ReferenceBodyTempK);
        float enthalpyFactor = totalMixtureCp / 29.0f;
        float thermalHazard = math.saturate((thermalDelta * enthalpyFactor) / 50.0f);

        CellPhysicalHazardLevels[0 * Stride + simIdx] = maxExplosionRisk;
        CellPhysicalHazardLevels[1 * Stride + simIdx] = math.saturate(totalRadioactivity * 10.0f);
        CellPhysicalHazardLevels[2 * Stride + simIdx] = causticHazard;
        CellPhysicalHazardLevels[3 * Stride + simIdx] = thermalHazard;

        // 2. Calculate Metabolism-Specific Danger Levels
        if (UpdateBiological)
        {
          for (int b = 0; b < BatchCount; b++)
          {
            var criteria = BatchDangerSpecs[b];
            float danger = 0;

            if (temperature < criteria.MinTemp)
            {
              float range = criteria.MinTemp - criteria.MinTempFullDanger;
              danger = math.max(danger, math.saturate((criteria.MinTemp - temperature) / math.max(0.001f, range)));
            }
            else if (temperature > criteria.MaxTemp)
            {
              float range = criteria.MaxTempFullDanger - criteria.MaxTemp;
              danger = math.max(danger, math.saturate((temperature - criteria.MaxTemp) / math.max(0.001f, range)));
            }

            if (pressure < criteria.MinPressure)
            {
              float range = criteria.MinPressure - criteria.MinPressureFullDanger;
              danger = math.max(danger, math.saturate((criteria.MinPressure - pressure) / math.max(0.001f, range)));
            }
            else if (pressure > criteria.MaxPressure)
            {
              float range = criteria.MaxPressureFullDanger - criteria.MaxPressure;
              danger = math.max(danger, math.saturate((pressure - criteria.MaxPressure) / math.max(0.001f, range)));
            }

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

            danger = math.max(danger, totalRadioactivity * (1.0f - criteria.RadiationResistance) * 2.0f);
            danger = math.max(danger, causticHazard);
            float bThermalDelta = math.max(0, temperature - criteria.MaxTemp);
            float bThermalHazard = math.saturate((bThermalDelta * enthalpyFactor * criteria.EnthalpySensitivity) / 50.0f);
            danger = math.max(danger, bThermalHazard);

            CellDangerLevels[b * Stride + simIdx] = danger;
          }
        }
      }
    }
  }
}
