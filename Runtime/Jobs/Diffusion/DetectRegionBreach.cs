using SolarWeb.Pneuma.Logging;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace SolarWeb.Pneuma.Jobs.Diffusion
{
  /// <summary>
  /// Parallel job that scans every region face after ComputeRegionGasDiffusion and detects
  /// high-flux "breach" events — large pressure differentials combined with significant gas
  /// throughput — that warrant splitting the constituent sim cells into dynamic mini-regions.
  ///
  /// When a breach is detected on region face F:
  ///   - All (simA, simB) pairs in RegionFaceToCellSim[F] are converted to world indices
  ///     and written to PendingSplitWorldIndices.
  ///
  /// One job instance is dispatched per region face (IJobParallelFor over TotalRegionFaceCount).
  /// </summary>
  [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
  public struct DetectRegionBreach : IJobParallelFor
  {
    [ReadOnly] public NativeArray<int> FaceRegionA;
    [ReadOnly] public NativeArray<int> FaceRegionB;
    [ReadOnly] public NativeArray<float> FaceGasFlux;
    [ReadOnly] public NativeArray<float> RegionPressureKpa;

    [ReadOnly] public NativeArray<int> RegionFaceToCellOffsets;
    [ReadOnly] public NativeArray<int> RegionFaceToCellCounts;
    [ReadOnly] public NativeArray<int> RegionFaceToCellSimA;
    [ReadOnly] public NativeArray<int> RegionFaceToCellSimB;

    [ReadOnly] public NativeArray<int> SimToWorldIndex;

    public NativeQueue<int>.ParallelWriter PendingSplitWorldIndices;

    public int GasCount;
    public int FaceStride;
    public int SentinelRegionIndex;
    public int SentinelCellIndex;
    public float BreachPThresholdKpa;
    public float BreachFluxThresholdUmol;

    public void Execute(int fIdx)
    {
      int rA = FaceRegionA[fIdx];
      int rB = FaceRegionB[fIdx];

      if (rA == SentinelRegionIndex && rB == SentinelRegionIndex) return;

      float pA = (rA == SentinelRegionIndex) ? 0f : RegionPressureKpa[rA];
      float pB = (rB == SentinelRegionIndex) ? 0f : RegionPressureKpa[rB];
      float deltaPAbs = math.abs(pA - pB);
      if (deltaPAbs < BreachPThresholdKpa) return;

      float totalFlux = 0f;
      for (int g = 0; g < GasCount; g++)
        totalFlux += math.abs(FaceGasFlux[g * FaceStride + fIdx]);

      if (totalFlux < BreachFluxThresholdUmol) return;

      int offset = RegionFaceToCellOffsets[fIdx];
      int count = RegionFaceToCellCounts[fIdx];
      for (int i = 0; i < count; i++)
      {
        int sA = RegionFaceToCellSimA[offset + i];
        int sB = RegionFaceToCellSimB[offset + i];
        if (sA != SentinelCellIndex) PendingSplitWorldIndices.Enqueue(SimToWorldIndex[sA]);
        if (sB != SentinelCellIndex) PendingSplitWorldIndices.Enqueue(SimToWorldIndex[sB]);
      }
    }
  }
}
