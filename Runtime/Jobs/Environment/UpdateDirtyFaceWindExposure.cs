using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace SolarWeb.Pneuma.Jobs.Environment
{
  [BurstCompile]
  public struct UpdateDirtyFaceWindExposure : IJobParallelForDefer
  {
    [ReadOnly] public NativeArray<int> DirtyFaceIndices;
    [ReadOnly] public NativeArray<int> RegionFaceToCellOffsets;
    [ReadOnly] public NativeArray<int> RegionFaceToCellCounts;
    [ReadOnly] public NativeArray<int> RegionFaceToCellSimA;
    [ReadOnly] public NativeArray<int> SimToWorldIndex;
    [ReadOnly] public NativeArray<float> CellWindExposure;
    [ReadOnly] public NativeArray<int3> FaceDirection;
    [ReadOnly] public NativeArray<float> FaceSurfaceArea;

    [NativeDisableParallelForRestriction] public NativeArray<float> FaceWindExposureX;
    [NativeDisableParallelForRestriction] public NativeArray<float> FaceWindExposureZ;
    [NativeDisableParallelForRestriction] public NativeArray<byte> IsFaceDirty;

    public void Execute(int idx)
    {
      int fIdx = DirtyFaceIndices[idx];
      float totalExposure = 0f;
      int count = RegionFaceToCellCounts[fIdx];
      int offset = RegionFaceToCellOffsets[fIdx];

      if (count > 0)
      {
        for (int i = 0; i < count; i++)
        {
          int simA = RegionFaceToCellSimA[offset + i];
          int worldA = SimToWorldIndex[simA];
          totalExposure += CellWindExposure[worldA];
        }
        float avgExposure = totalExposure / count;
        int3 dir = FaceDirection[fIdx];
        float area = FaceSurfaceArea[fIdx];

        FaceWindExposureX[fIdx] = dir.x * avgExposure * area;
        FaceWindExposureZ[fIdx] = dir.z * avgExposure * area;
      }
      IsFaceDirty[fIdx] = 0;
    }
  }
}
