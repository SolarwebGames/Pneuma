using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace SolarWeb.Pneuma.Jobs.Environment
{
  [BurstCompile]
  public struct UpdateFaceWindExposure : IJobParallelFor
  {
    [ReadOnly] public NativeArray<int> FaceRegionA;
    [ReadOnly] public NativeArray<int> FaceRegionB;
    [ReadOnly] public NativeArray<int> RegionFaceToCellOffsets;
    [ReadOnly] public NativeArray<int> RegionFaceToCellCounts;
    [ReadOnly] public NativeArray<int> RegionFaceToCellSimA;
    [ReadOnly] public NativeArray<int> RegionFaceToCellSimB;
    [ReadOnly] public NativeArray<int> SimToWorldIndex;
    [ReadOnly] public NativeArray<float> CellWindExposure;
    [ReadOnly] public NativeArray<int3> FaceDirection;
    [ReadOnly] public NativeArray<float> FaceSurfaceArea;

    [WriteOnly] public NativeArray<float> FaceWindExposureX;
    [WriteOnly] public NativeArray<float> FaceWindExposureZ;

    public int SentinelRegionIndex;

    public void Execute(int fIdx)
    {
      float totalExposure = 0f;
      int count = RegionFaceToCellCounts[fIdx];
      int offset = RegionFaceToCellOffsets[fIdx];

      if (count == 0)
      {
        FaceWindExposureX[fIdx] = 0;
        FaceWindExposureZ[fIdx] = 0;
        return;
      }

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
  }
}
