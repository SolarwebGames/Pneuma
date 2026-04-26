using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

using SolarWeb.Pneuma.Data;

namespace SolarWeb.Pneuma.Jobs.UI
{
  [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
  public struct PrepareStressVFX : IJob
  {
    [ReadOnly] public NativeArray<int> FaceRegionA;
    [ReadOnly] public NativeArray<int> FaceRegionB;
    [ReadOnly] public NativeArray<float> FaceMaxPressureDeltaKpa;
    [ReadOnly] public NativeArray<float> RegionPressureKpa;
    [ReadOnly] public NativeArray<int> RegionFaceToCellOffsets;
    [ReadOnly] public NativeArray<int> RegionFaceToCellCounts;
    [ReadOnly] public NativeArray<int> RegionFaceToCellSimA;
    [ReadOnly] public NativeArray<int> RegionFaceToCellSimB;
    [ReadOnly] public NativeArray<int> SimToWorldIndex;

    public NativeList<StressPointData> OutStressPoints;

    public int FaceCount;
    public int MapWidth;
    public int SentinelRegionIndex;
    public int SentinelCellIndex;

    public void Execute()
    {
      for (int fIdx = 0; fIdx < FaceCount; fIdx++)
      {
        float rating = FaceMaxPressureDeltaKpa[fIdx];
        if (rating >= 10000f) continue; // Indestructible

        int rA = FaceRegionA[fIdx];
        int rB = FaceRegionB[fIdx];

        float pA = (rA >= 0 && rA < RegionPressureKpa.Length) ? RegionPressureKpa[rA] : 0f;
        float pB = (rB >= 0 && rB < RegionPressureKpa.Length) ? RegionPressureKpa[rB] : 0f;
        float deltaP = math.abs(pA - pB);

        float load = deltaP / math.max(1f, rating);
        if (load > 0.85f)
        {
          // Find a representative world position for this face
          int offset = RegionFaceToCellOffsets[fIdx];
          int count = RegionFaceToCellCounts[fIdx];
          if (count <= 0) continue;

          float3 center = float3.zero;
          float3 normal = float3.zero;

          for (int i = 0; i < count; i++)
          {
            int simA = RegionFaceToCellSimA[offset + i];
            int simB = RegionFaceToCellSimB[offset + i];

            float3 posA = GetWorldPos(simA);
            float3 posB = (simB == SentinelCellIndex) ? posA : GetWorldPos(simB);

            center += (posA + posB) * 0.5f;
            normal += (posB - posA);
          }

          OutStressPoints.Add(new StressPointData
          {
            Position = center / count,
            Normal = math.normalize(normal / count),
            Intensity = math.saturate((load - 0.85f) / 0.15f)
          });
        }
      }
    }

    private float3 GetWorldPos(int simIdx)
    {
      int worldIdx = SimToWorldIndex[simIdx];
      return new float3((worldIdx % MapWidth) + 0.5f, 0, (worldIdx / MapWidth) + 0.5f);
    }
  }

  [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
  public struct PrepareThermalVFXJob : IJob
  {
    [ReadOnly] public NativeArray<float> TemperatureK;
    [ReadOnly] public NativeArray<int> ActiveRegionIndices;
    [ReadOnly] public NativeArray<int> RegionMinX;
    [ReadOnly] public NativeArray<int> RegionMaxX;
    [ReadOnly] public NativeArray<int> RegionMinZ;
    [ReadOnly] public NativeArray<int> RegionMaxZ;

    public NativeList<ThermalPointData> OutThermalPoints;

    public int ActiveRegionCount;

    public void Execute()
    {
      for (int i = 0; i < ActiveRegionCount; i++)
      {
        int rIdx = ActiveRegionIndices[i];
        float temp = TemperatureK[rIdx];

        // Only emit for extreme temperatures
        if (temp > 330f || temp < 255f)
        {
          float centerX = (RegionMinX[rIdx] + RegionMaxX[rIdx]) * 0.5f;
          float centerZ = (RegionMinZ[rIdx] + RegionMaxZ[rIdx]) * 0.5f;

          OutThermalPoints.Add(new ThermalPointData
          {
            Position = new float3(centerX, 0, centerZ),
            Temperature = temp
          });
        }
      }
    }
  }
}
