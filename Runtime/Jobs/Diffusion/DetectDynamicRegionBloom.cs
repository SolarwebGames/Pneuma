using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace SolarWeb.Pneuma.Jobs.Diffusion
{
  [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
  public struct DetectDynamicRegionBloom : IJobParallelFor
  {
    [ReadOnly] public NativeArray<int> FaceRegionA;
    [ReadOnly] public NativeArray<int> FaceRegionB;
    [ReadOnly] public NativeArray<int> FaceSourceEdge;

    [ReadOnly] public NativeArray<float> PressureKpa;

    [ReadOnly] public NativeArray<int> CellFaceCellA;
    [ReadOnly] public NativeArray<int> CellFaceCellB;
    [ReadOnly] public NativeArray<int> SimToWorldIndex;
    [ReadOnly] public NativeArray<int> WorldToSimIndex;

    [ReadOnly] public NativeArray<int> DynRegionWorldIdx;

    public int DynamicRegionPoolStart;
    public int SentinelRegionIndex;
    public int SentinelCellIndex;

    public float BloomPressureRatio;
    public float BloomAbsoluteKpa;

    public NativeQueue<int>.ParallelWriter SplitQueue;

    public void Execute(int i)
    {
      int regA = FaceRegionA[i];
      int regB = FaceRegionB[i];

      if (regA == SentinelRegionIndex && regB == SentinelRegionIndex) return;

      bool aIsDynamic = regA >= DynamicRegionPoolStart && regA < SentinelRegionIndex;
      bool bIsDynamic = regB >= DynamicRegionPoolStart && regB < SentinelRegionIndex;

      if (aIsDynamic == bIsDynamic) return;

      int dynReg = aIsDynamic ? regA : regB;
      int staticReg = aIsDynamic ? regB : regA;

      if (staticReg == SentinelRegionIndex) return;

      float pDyn = PressureKpa[dynReg];
      float pStatic = PressureKpa[staticReg];

      if (pDyn > pStatic * BloomPressureRatio && (pDyn - pStatic) > BloomAbsoluteKpa)
      {
        int edgeIdx = FaceSourceEdge[i];
        if (edgeIdx < 0) return;

        int simA = CellFaceCellA[edgeIdx];
        int simB = CellFaceCellB[edgeIdx];

        int dynSlot = dynReg - DynamicRegionPoolStart;
        int dynWorldIdx = DynRegionWorldIdx[dynSlot];

        if (dynWorldIdx < 0) return;

        int dynSimIdx = WorldToSimIndex[dynWorldIdx];

        int neighborSimIdx = (simA == dynSimIdx) ? simB : simA;

        if (neighborSimIdx != SentinelCellIndex)
        {
          int neighborWorldIdx = SimToWorldIndex[neighborSimIdx];
          SplitQueue.Enqueue(neighborWorldIdx);
        }
      }
    }
  }
}
