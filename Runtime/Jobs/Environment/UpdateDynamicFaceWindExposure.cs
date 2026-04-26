using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace SolarWeb.Pneuma.Jobs.Environment
{
  [BurstCompile]
  public struct UpdateDynamicFaceWindExposure : IJobParallelFor
  {
    [ReadOnly] public NativeArray<int> FaceRegionA;
    [ReadOnly] public NativeArray<int> FaceRegionB;
    [ReadOnly] public NativeArray<int> DynRegionWorldIdx;
    [ReadOnly] public NativeArray<float> CellWindExposure;
    public int DynamicRegionPoolStart;
    public int SentinelRegionIndex;

    [WriteOnly] public NativeArray<float> FaceWindExposureX;
    [WriteOnly] public NativeArray<float> FaceWindExposureZ;

    public void Execute(int fIdx)
    {
      int iA = FaceRegionA[fIdx];
      int iB = FaceRegionB[fIdx];

      int worldIdx = -1;
      if (iA >= DynamicRegionPoolStart && iA < SentinelRegionIndex)
      {
        worldIdx = DynRegionWorldIdx[iA - DynamicRegionPoolStart];
      }
      else if (iB >= DynamicRegionPoolStart && iB < SentinelRegionIndex)
      {
        worldIdx = DynRegionWorldIdx[iB - DynamicRegionPoolStart];
      }

      if (worldIdx == -1)
      {
        FaceWindExposureX[fIdx] = 0;
        FaceWindExposureZ[fIdx] = 0;
        return;
      }

      // float exposure = CellWindExposure[worldIdx];
      FaceWindExposureX[fIdx] = 0;
      FaceWindExposureZ[fIdx] = 0;
    }
  }
}
