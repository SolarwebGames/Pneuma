using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace SolarWeb.Pneuma.Jobs.PhaseTransition
{
  /// <summary>
  /// Computes freezing and melting for all condensable/freezable gases in each active region.
  ///
  /// If T < MeltingPointK: Liquid -> Solid (Freezing)
  /// If T > MeltingPointK: Solid -> Liquid (Melting)
  /// </summary>
  [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
  public struct ComputeSolidPhaseTransitions : IJobParallelForDefer
  {
    [ReadOnly] public NativeArray<int> ActiveRegionIndices;
    [ReadOnly] public NativeArray<float> TemperatureK;
    [ReadOnly] public NativeArray<float> MeltingPointK;

    // Stride-based indexing: [gasId * RegionStride + regionIdx]
    public NativeArray<long> LiquidUMoles;
    public NativeArray<long> SolidUMoles;

    public int GasCount;
    public int RegionStride;
    public int SentinelIndex;
    public float TimeStep;
    public float FreezingRate; // [s^-1]
    public float MeltingRate;  // [s^-1]

    public void Execute(int index)
    {
      int simIdx = ActiveRegionIndices[index];
      if (simIdx == SentinelIndex) return;

      float tempK = TemperatureK[simIdx];

      for (int g = 0; g < GasCount; g++)
      {
        float mp = MeltingPointK[g];
        if (mp <= 0f) continue; // skip gases without defined melting point

        int dataIdx = (g * RegionStride) + simIdx;
        long liquidUMol = LiquidUMoles[dataIdx];
        long solidUMol = SolidUMoles[dataIdx];

        if (tempK < mp && liquidUMol > 0)
        {
          // Freezing: Liquid -> Solid
          long transfer = (long)(liquidUMol * FreezingRate * TimeStep);
          transfer = math.max(1L, transfer); // ensure progress
          transfer = math.min(transfer, liquidUMol);

          LiquidUMoles[dataIdx] = liquidUMol - transfer;
          SolidUMoles[dataIdx] = solidUMol + transfer;
        }
        else if (tempK > mp && solidUMol > 0)
        {
          // Melting: Solid -> Liquid
          long transfer = (long)(solidUMol * MeltingRate * TimeStep);
          transfer = math.max(1L, transfer); // ensure progress
          transfer = math.min(transfer, solidUMol);

          LiquidUMoles[dataIdx] = liquidUMol + transfer;
          SolidUMoles[dataIdx] = solidUMol - transfer;
        }
      }
    }
  }
}
