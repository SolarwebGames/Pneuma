using SolarWeb.Pneuma.Constants;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace SolarWeb.Pneuma.Jobs.Diffusion
{
  /// <summary>
  /// Fused sync + precompute pass. Replaces the former SyncRegionTotalMoles + PrecomputeRegionPhysics
  /// pair, eliminating a second full stream of the uMoles array.
  ///
  /// Two gas-outer / region-inner passes over the batch:
  ///   Pass 1 — accumulate TotalUMoles and mass-weighted totals.
  ///   Pass 2 — compute MolarFractions and Cp/Cv mixture fractions (uMoles hot in L1 from pass 1).
  /// Per-region finalize writes pressure, activity hysteresis, and derived physics.
  /// Derived-physics writes (SpeedOfSound, TFactor, etc.) are skipped for quiescent regions.
  /// </summary>
  [BurstCompile]
  public unsafe struct SyncAndPrecomputeRegions : IJobParallelForBatch
  {
    public const int MaxBatch = 128;

    [ReadOnly] public NativeArray<long> uMoles;
    [ReadOnly] public NativeArray<float> GasMolarMassesScaled;      // MolarMass * 1e-6f
    [ReadOnly] public NativeArray<float> RegionVolumes;
    [ReadOnly] public NativeArray<float> TemperatureK;
    [ReadOnly] public NativeArray<float> StructuralTemperatureK;
    [ReadOnly] public NativeArray<float> MolarHeatCapacityAtConstantPressure;
    [ReadOnly] public NativeArray<float> MolarHeatCapacityAtConstantVolume;
    [ReadOnly] public NativeArray<bool> IsBurning;
    public float AmbientTemp;

    public NativeArray<long> TotalUMoles;
    public NativeArray<float> AverageMolarMass;
    public NativeArray<float> PressureKpa;
    public NativeArray<float> PreviousPressureKpa;
    [NativeDisableParallelForRestriction] public NativeArray<float> PreviousTemperatureK;
    [NativeDisableParallelForRestriction] public NativeArray<float> PreviousStructuralTemperatureK;
    public NativeArray<int> ActiveTicks;
    public NativeArray<bool> RegionIsActive;

    public NativeArray<float> InvTotalUMoles;
    public NativeArray<float> RegionSpeedOfSound;
    public NativeArray<float> RegionInvVolConst;
    public NativeArray<float> RegionTFactor;
    public NativeArray<float> MixtureMolarCp;
    public NativeArray<float> MolarFractions;   // GasCount * RegionStride

    public int GasCount;
    public int RegionStride;
    public float PressureDirtyThreshold;
    public float TemperatureDirtyThreshold;
    public int HysteresisTicks;
    public int SentinelIndex;

    public void Execute(int startIndex, int count)
    {
      long* tUMoles = stackalloc long[MaxBatch];
      float* tMass = stackalloc float[MaxBatch];
      float* tCp = stackalloc float[MaxBatch];
      float* tCv = stackalloc float[MaxBatch];
      float* invTotal = stackalloc float[MaxBatch];

      for (int j = 0; j < count; j++) { tUMoles[j] = 0; tMass[j] = 0f; tCp[j] = 0f; tCv[j] = 0f; }

      // Pass 1: accumulate totals — gas-outer, region-inner for sequential uMoles reads.
      for (int g = 0; g < GasCount; g++)
      {
        int rowBase = g * RegionStride + startIndex;
        float mmScaled = GasMolarMassesScaled[g];
#if DEBUG && UNITY_BURST_EXPERIMENTAL_LOOP_INTRINSICS
        Unity.Burst.CompilerServices.Loop.ExpectVectorized();
#endif
        for (int j = 0; j < count; j++)
        {
          long m = uMoles[rowBase + j];
          tUMoles[j] += m;
          tMass[j] += m * mmScaled;
        }
      }

      for (int j = 0; j < count; j++)
        invTotal[j] = tUMoles[j] > 0 ? 1f / (float)tUMoles[j] : 0f;

      // Pass 2: molar fractions + Cp/Cv — uMoles is hot in L1 from pass 1.
      for (int g = 0; g < GasCount; g++)
      {
        int rowBase = g * RegionStride + startIndex;
        float cp = MolarHeatCapacityAtConstantPressure[g];
        float cv = MolarHeatCapacityAtConstantVolume[g];
#if DEBUG && UNITY_BURST_EXPERIMENTAL_LOOP_INTRINSICS
        Unity.Burst.CompilerServices.Loop.ExpectVectorized();
#endif
        for (int j = 0; j < count; j++)
        {
          float frac = uMoles[rowBase + j] * invTotal[j];
          MolarFractions[rowBase + j] = frac;
          tCp[j] += frac * cp;
          tCv[j] += frac * cv;
        }
      }

      const float Rf = 8.314462618f;
      float safeAmbient = math.max(AmbientTemp, 0.01f);

      for (int j = 0; j < count; j++)
      {
        int simIdx = startIndex + j;
        long totalUMoles = tUMoles[j];
        float totalMass = tMass[j];
        float volume = RegionVolumes[simIdx];
        float tempK = TemperatureK[simIdx];
        float structTempK = StructuralTemperatureK[simIdx];

        float pressureKpa = 0f;
        if (volume >= 0.001f)
          pressureKpa = (totalUMoles * 1e-6f * Rf * tempK) / (volume * 1000f);

        float deltaP = math.abs(pressureKpa - PreviousPressureKpa[simIdx]);
        float deltaT = math.abs(tempK - PreviousTemperatureK[simIdx]);
        float deltaST = math.abs(structTempK - PreviousStructuralTemperatureK[simIdx]);

        PreviousTemperatureK[simIdx] = tempK;
        PreviousStructuralTemperatureK[simIdx] = structTempK;

        bool isDirty = deltaP > PressureDirtyThreshold
                    || deltaT > TemperatureDirtyThreshold
                    || deltaST > TemperatureDirtyThreshold
                    || IsBurning[simIdx] || simIdx == SentinelIndex;
        int priorActiveTicks = ActiveTicks[simIdx];

        if (isDirty) ActiveTicks[simIdx] = HysteresisTicks;
        else if (priorActiveTicks > 0) ActiveTicks[simIdx]--;

        RegionIsActive[simIdx] = volume > 0f;
        TotalUMoles[simIdx] = totalUMoles;
        PressureKpa[simIdx] = pressureKpa;
        PreviousPressureKpa[simIdx] = pressureKpa;
        AverageMolarMass[simIdx] = totalUMoles > 0
            ? totalMass / (totalUMoles * 1e-6f) : 28.97f;

        // Derived-physics outputs only need updating when the region is active.
        if (!isDirty && priorActiveTicks == 0) continue;

        InvTotalUMoles[simIdx] = invTotal[j];

        float gamma = tCp[j] / math.max(tCv[j], 0.0001f);
        float molarMass = math.max(AverageMolarMass[simIdx], 1f);
        RegionSpeedOfSound[simIdx] = math.sqrt(gamma * AtmosphereConstants.R1K * math.max(tempK, 0.01f) / molarMass);

        float tRatio = math.max(tempK, 0.01f) / safeAmbient;
        RegionTFactor[simIdx] = math.pow(tRatio, 0.75f);
        RegionInvVolConst[simIdx] = volume > 0.001f ? (AtmosphereConstants.Rf / volume) : 0f;
        MixtureMolarCp[simIdx] = tCp[j];
      }
    }
  }
}
