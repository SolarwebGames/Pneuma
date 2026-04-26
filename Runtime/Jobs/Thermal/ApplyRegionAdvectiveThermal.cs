using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

using SolarWeb.Pneuma.MathA;

namespace SolarWeb.Pneuma.Jobs.Thermal
{
  /// <summary>
  /// Applies advective (mass-transport) heat to each static region.
  ///
  /// Advective heat is asymmetric: only the receiving region's temperature changes.
  /// The source region loses mass but its remaining gas stays at the same temperature
  /// (homogeneous loss). The conductive term (already applied by ApplyRegionThermalFlux)
  /// is symmetric and correct with the ±1 sign convention.
  ///
  /// Reads <see cref="TemperatureKSnapshot"/> (= PreviousTemperatureK, the pre-tick snapshot
  /// set by SyncRegionTotalMoles) for all temperature look-ups so that concurrent threads
  /// processing neighbouring regions don't observe each other's in-progress writes.
  /// Each thread writes only to its own TemperatureK[simIdx] slot — no races.
  /// </summary>
  [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
  public struct ApplyRegionAdvectiveThermal : IJobParallelForDefer
  {
    [ReadOnly] public NativeArray<int> RegionFaceOffsets;
    [ReadOnly] public NativeArray<int> RegionFaceCounts;
    [ReadOnly] public NativeArray<int> RegionFaceIndices;
    [ReadOnly] public NativeArray<int> FaceRegionA;
    [ReadOnly] public NativeArray<int> FaceRegionB;
    [ReadOnly] public NativeArray<float> FaceGasFlux;            // layout: g * FaceStride + fIdx
    [ReadOnly] public NativeArray<float> GasMolarHeatCapacityCp;
    [ReadOnly] public NativeArray<float> TemperatureKSnapshot;   // PreviousTemperatureK — pre-tick
    [ReadOnly] public NativeArray<float> MixtureMolarCp;
    [ReadOnly] public NativeArray<long> TotalUMoles;
    [ReadOnly] public NativeArray<float> PressureKpa;
    [ReadOnly] public NativeArray<float> RegionVolumes;
    [ReadOnly] public NativeArray<int> ActiveRegionIndices;

    [ReadOnly] public NativeArray<float> FaceActivePumpRate;
    [ReadOnly] public NativeArray<sbyte> FaceFlowDirection;
    [ReadOnly] public NativeArray<float> FaceMaxPumpPressureKpa;

    public NativeArray<float> TemperatureK;

    public int GasCount;
    public int FaceStride;
    public int SentinelIndex;
    public float TimeStep;

    public void Execute(int index)
    {
      int simIdx = ActiveRegionIndices[index];
      if (simIdx == SentinelIndex) return;

      int offset = RegionFaceOffsets[simIdx];
      int count = RegionFaceCounts[simIdx];
      if (count == 0) return;

      float tSelf = TemperatureKSnapshot[simIdx];
      float pSelf = PressureKpa[simIdx];
      float advJoules = 0f;

      for (int i = 0; i < count; i++)
      {
        int fIdx = RegionFaceIndices[offset + i];

        float totalHeatCapFlux = 0f;
        float totalUMolFlux = 0f;
        for (int g = 0; g < GasCount; g++)
        {
          float flux = FaceGasFlux[g * FaceStride + fIdx];
          totalHeatCapFlux += flux * GasMolarHeatCapacityCp[g];
          totalUMolFlux += flux;
        }

        // inflowFlux > 0 means gas is flowing INTO simIdx.
        bool isRegionA = (FaceRegionA[fIdx] == simIdx);
        float direction = isRegionA ? -1.0f : 1.0f;
        float inflowHeatCapFlux = totalHeatCapFlux * direction;
        float inflowUMoles = totalUMolFlux * direction;

        int nbr = isRegionA ? FaceRegionB[fIdx] : FaceRegionA[fIdx];

        // Handle pump work heating
        if (FaceActivePumpRate[fIdx] > 0f)
        {
          sbyte flowDir = FaceFlowDirection[fIdx];
          bool pumpPushingIntoMe = (isRegionA && flowDir < 0) || (!isRegionA && flowDir > 0);
          if (pumpPushingIntoMe)
          {
            advJoules += ThermalMath.CalculatePumpWorkJ(
                pSelf, PressureKpa[nbr], TemperatureKSnapshot[nbr],
                inflowUMoles, FaceActivePumpRate[fIdx], TimeStep, FaceMaxPumpPressureKpa[fIdx]);
          }
        }

        advJoules += ThermalMath.CalculateAdvectiveJoules(inflowHeatCapFlux, TemperatureKSnapshot[nbr], tSelf);
      }

      float temp = TemperatureK[simIdx];
      ThermalMath.ApplyHeat(ref temp, (float)TotalUMoles[simIdx], RegionVolumes[simIdx], MixtureMolarCp[simIdx], advJoules);
      TemperatureK[simIdx] = temp;
    }
  }
}
