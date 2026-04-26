using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

using SolarWeb.Pneuma.Logging;

namespace SolarWeb.Pneuma.Jobs.Metabolism
{
  [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
  public struct SimplifiedMetabolismJob : IJobParallelFor
  {
    public float TimeStep;
    public int GasCount;
    public int RegionStride;
    public int SentinelRegionIndex;

    // --- Precomputed Species Weights ---
    [ReadOnly] public NativeArray<float> ToxWeights;
    [ReadOnly] public NativeArray<float> CausticWeights;
    [ReadOnly] public NativeArray<float> BioInterferenceWeights;
    [ReadOnly] public NativeArray<float> RadWeights;
    public int VitalGasId;
    public float LethalThreshold;
    public float SuffocationPenalty;
    public float RecoveryRate;

    public float RadiationSensitivity;
    public float CausticSensitivity;

    public float MaxPressure;
    public float MaxPressureFullDanger;

    public float MinPressure;
    public float MinPressureFullDanger;

    // --- Gas Exchange ---
    public long VitalConsumptionRateUMol;
    public long ByproductProductionRateUMol;
    public int ByproductGasId;
    [ReadOnly] public NativeArray<int> ExchangerPersistentIds; // [i*2+0]=vital, [i*2+1]=byproduct
    [ReadOnly] public NativeArray<int> GlobalIndexLookup;
    [NativeDisableParallelForRestriction] public NativeArray<long> BufferDesiredFluxUMol;
    [NativeDisableParallelForRestriction] public NativeArray<long> BufferInternalMicromoles;

    // --- Grid Data ---
    [ReadOnly] public NativeArray<int> WorldToRegionIndex;
    [ReadOnly] public NativeArray<float> MolarFractions; // [g * RegionStride + rIdx]
    [ReadOnly] public NativeArray<float> PressureKpa;    // [rIdx]

    // --- Entity SoA ---
    [ReadOnly] public NativeArray<int> WorldIndices;
    [ReadOnly] public NativeArray<float> ToxicResistances;
    [ReadOnly] public NativeArray<float> CorrosiveResistances;
    [ReadOnly] public NativeArray<float> RadiationResistances;
    [ReadOnly] public NativeArray<float> PressureResistances;
    [ReadOnly] public NativeArray<float> VacuumResistances;

    public NativeArray<float> Integrity;

    public void Execute(int i)
    {
      int worldIdx = WorldIndices[i];
      int regIdx = (worldIdx >= 0 && worldIdx < WorldToRegionIndex.Length) ? WorldToRegionIndex[worldIdx] : -1;

      if (regIdx < 0) regIdx = SentinelRegionIndex;

      float stress = 0;
      float rawStress = 0f;
      float vitalFrac = 0f;

      for (int g = 0; g < GasCount; g++)
      {
        float fraction = MolarFractions[g * RegionStride + regIdx];

        float rawTox = fraction * ToxWeights[g];
        float rawCau = fraction * CausticWeights[g];
        float rawBio = fraction * BioInterferenceWeights[g];
        float rawRad = fraction * RadWeights[g];

        // Apply precomputed weights with per-entity resistances
        float tox = rawTox * (1.0f - ToxicResistances[i]);
        float cau = rawCau * (1.0f - CorrosiveResistances[i]) * CausticSensitivity;
        float bio = rawBio * (1.0f - CorrosiveResistances[i]);
        float rad = rawRad * (1.0f - RadiationResistances[i]) * RadiationSensitivity;

        rawStress += (rawTox + rawCau + rawBio + rawRad);
        stress += (tox + cau + bio + rad);

        if (g == VitalGasId) vitalFrac = fraction;

      }

      // Check for vital gas presence (Hypoxia)
      bool vitalsOk = true;
      if (VitalGasId >= 0)
      {
        if (vitalFrac < LethalThreshold)
        {
          stress += SuffocationPenalty;
          vitalsOk = false;
        }
      }

      // Check for overpressure stress
      float currentPressure = (regIdx >= 0) ? PressureKpa[regIdx] : 0f;
      float resistance = PressureResistances[i];
      float effectiveMax = MaxPressure + resistance;

      if (currentPressure > effectiveMax)
      {
        float range = (MaxPressureFullDanger + resistance) - effectiveMax;
        float pStress = math.saturate((currentPressure - effectiveMax) / math.max(0.001f, range));
        stress += pStress * 0.1f; // 0.1 stress = 10% integrity loss per second at full danger
      }

      // Check for low-pressure stress (Vacuum exposure)
      float vacuumRes = VacuumResistances[i];
      float effectiveMin = MinPressure * (1.0f - math.saturate(vacuumRes));
      float effectiveMinFull = MinPressureFullDanger * (1.0f - math.saturate(vacuumRes));

      if (currentPressure < effectiveMin)
      {
        float range = effectiveMin - effectiveMinFull;
        float vStress = math.saturate((effectiveMin - currentPressure) / math.max(0.001f, range));
        stress += vStress * 0.1f;
      }

      float recovery = (vitalsOk && rawStress <= 0f && currentPressure <= effectiveMax && currentPressure >= effectiveMin) ? RecoveryRate * TimeStep : 0f;
      Integrity[i] = math.saturate(Integrity[i] - stress * TimeStep + recovery);

      // --- Gas Exchange via ExchangerBuffer ---
      bool isSentinel = (regIdx == SentinelRegionIndex);

      // O2 inhale: drain what was delivered last cycle (consume it), then set demand for next cycle
      if (VitalGasId >= 0)
      {
        int o2PId = ExchangerPersistentIds[i * 2 + 0];
        if (o2PId >= 0)
        {
          int bufIdx = GlobalIndexLookup[o2PId];
          if (bufIdx >= 0)
          {
            BufferInternalMicromoles[bufIdx] = 0; // Consume the O2 delivered by the previous exchanger run
            BufferDesiredFluxUMol[bufIdx] = isSentinel ? 0L : -VitalConsumptionRateUMol;
          }
        }
      }

      // CO2 exhale: accumulate production into InternalMicromoles; exchanger drains on its due tick
      if (ByproductGasId >= 0 && !isSentinel)
      {
        int co2PId = ExchangerPersistentIds[i * 2 + 1];
        if (co2PId >= 0)
        {
          int bufIdx = GlobalIndexLookup[co2PId];
          if (bufIdx >= 0)
          {
            BufferInternalMicromoles[bufIdx] += (long)(ByproductProductionRateUMol * TimeStep);
            BufferDesiredFluxUMol[bufIdx] = ByproductProductionRateUMol;
          }
        }
      }
    }
  }
}
