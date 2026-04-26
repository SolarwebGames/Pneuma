using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace SolarWeb.Pneuma.Jobs.Environment
{
  [BurstCompile]
  public struct IdentifyAffectedFaces : IJobParallelFor
  {

    [ReadOnly] public NativeArray<int> DirtyCellIndices;
    [ReadOnly] public NativeArray<int> WorldToSimIndex;
    public int SentinelCellIndex;

    [ReadOnly] public NativeArray<int> WorldToRegionIndex;
    [ReadOnly] public NativeArray<int> RegionFaceOffsets;
    [ReadOnly] public NativeArray<int> RegionFaceCounts;
    [ReadOnly] public NativeArray<int> RegionFaceIndices;

    [NativeDisableParallelForRestriction] public NativeArray<byte> IsFaceDirty;
    public NativeList<int>.ParallelWriter DirtyFaces;

    public void Execute(int idx)
    {
      int worldIdx = DirtyCellIndices[idx];
      int simIdx = WorldToSimIndex[worldIdx];
      if (simIdx == SentinelCellIndex) return;

      int regionIdx = WorldToRegionIndex[worldIdx];
      if (regionIdx < 0) return;

      int offset = RegionFaceOffsets[regionIdx];
      int count = RegionFaceCounts[regionIdx];
      for (int k = 0; k < count; k++)
      {
        int faceIdx = RegionFaceIndices[offset + k];
        if (IsFaceDirty[faceIdx] == 0)
        {
          IsFaceDirty[faceIdx] = 1;
          DirtyFaces.AddNoResize(faceIdx);
        }
      }
    }
  }
}
