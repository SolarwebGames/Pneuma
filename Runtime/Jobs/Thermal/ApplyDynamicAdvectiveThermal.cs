using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

using SolarWeb.Pneuma.MathA;

namespace SolarWeb.Pneuma.Jobs.Thermal
{
  /// <summary>
  /// Applies advective heat for dynamic faces to the receiving region only.
  /// Sequential IJob — dynamic faces are few and the receiving region could be
  /// a static region already touched by ApplyRegionAdvectiveThermal, so this must
  /// run after that job completes.
  ///
  /// Uses <see cref="TemperatureKSnapshot"/> (PreviousTemperatureK) for all
  /// temperature reads so every face sees the same pre-tick state regardless of
  /// the order faces are processed.
  /// </summary>
  [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
  public struct ApplyDynamicAdvectiveThermal : IJob
  {
    [ReadOnly] public NativeArray<int>   FaceRegionA;
    [ReadOnly] public NativeArray<int>   FaceRegionB;
    [ReadOnly] public NativeArray<float> FaceGasFlux;            // layout: g * FaceCount + fIdx
    [ReadOnly] public NativeArray<float> GasMolarHeatCapacityCp;
    [ReadOnly] public NativeArray<float> TemperatureKSnapshot;   // PreviousTemperatureK — pre-tick
    [ReadOnly] public NativeArray<float> MixtureMolarCp;
    [ReadOnly] public NativeArray<long>  TotalUMoles;
    [ReadOnly] public NativeArray<float> PressureKpa;
    [ReadOnly] public NativeArray<float> RegionVolumes;

    [ReadOnly] public NativeArray<float> FaceActivePumpRate;
    [ReadOnly] public NativeArray<sbyte> FaceFlowDirection;
    [ReadOnly] public NativeArray<float> FaceMaxPumpPressureKpa;

    public NativeArray<float> TemperatureK;

    public int GasCount;
    public int FaceCount;
    public int SentinelRegionIndex;
    public float TimeStep;

    public void Execute()
    {
      for (int fIdx = 0; fIdx < FaceCount; fIdx++)
      {
        int iA = FaceRegionA[fIdx];
        int iB = FaceRegionB[fIdx];

        if (iA == SentinelRegionIndex && iB == SentinelRegionIndex) continue;

        float totalHeatCapFlux = 0f;
        float totalUMolFlux = 0f;
        for (int g = 0; g < GasCount; g++)
        {
          float flux = FaceGasFlux[g * FaceCount + fIdx];
          totalHeatCapFlux += flux * GasMolarHeatCapacityCp[g];
          totalUMolFlux += flux;
        }

        float pumpRate = FaceActivePumpRate[fIdx];
        sbyte flowDir = FaceFlowDirection[fIdx];
        float maxPumpPressure = FaceMaxPumpPressureKpa[fIdx];

        // Add pump work heating
        if (pumpRate > 0f && flowDir != 0)
        {
            int dest = (flowDir > 0) ? iB : iA;
            int src  = (flowDir > 0) ? iA : iB;
            float direction = (flowDir > 0) ? 1.0f : -1.0f;
            float inflowUMoles = totalUMolFlux * direction;

            if (dest != SentinelRegionIndex && inflowUMoles > 1e-9f)
            {
                float pDest = PressureKpa[dest];
                float pSrc  = (src != SentinelRegionIndex) ? PressureKpa[src] : pDest;
                float tSrc  = (src != SentinelRegionIndex) ? TemperatureKSnapshot[src] : TemperatureKSnapshot[dest];

                float pumpWorkJ = ThermalMath.CalculatePumpWorkJ(
                    pDest, pSrc, tSrc, inflowUMoles, pumpRate, TimeStep, FaceMaxPumpPressureKpa[fIdx]);

                float temp = TemperatureK[dest];
                ThermalMath.ApplyHeat(ref temp, (float)TotalUMoles[dest], RegionVolumes[dest], MixtureMolarCp[dest], pumpWorkJ);
                TemperatureK[dest] = temp;
            }
        }

        if (totalHeatCapFlux == 0f) continue;

        // Positive flux = A→B; receiving region gets the advective heat.
        int receiving = (totalHeatCapFlux > 0f) ? iB : iA;
        int sending   = (totalHeatCapFlux > 0f) ? iA : iB;

        if (receiving == SentinelRegionIndex) continue;

        float inflowFlux = math.abs(totalHeatCapFlux);
        float tNeighbor  = TemperatureKSnapshot[sending];
        float tSelf      = TemperatureKSnapshot[receiving];
        float advJoules  = ThermalMath.CalculateAdvectiveJoules(inflowFlux, tNeighbor, tSelf);

        if (advJoules > 0f)
        {
          float temp = TemperatureK[receiving];
          ThermalMath.ApplyHeat(ref temp, (float)TotalUMoles[receiving], RegionVolumes[receiving], MixtureMolarCp[receiving], advJoules);
          TemperatureK[receiving] = temp;
        }
      }
    }
  }
}
