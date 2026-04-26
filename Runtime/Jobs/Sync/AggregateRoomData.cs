using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace SolarWeb.Pneuma.Jobs.Sync
{
  [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
  public struct AggregateRoomData : IJob
  {
    [ReadOnly] public NativeArray<int> RegionToRoomIndex;
    [ReadOnly] public NativeArray<float> RegionVolumes;
    [ReadOnly] public NativeArray<float> RegionPressureKpa;
    [ReadOnly] public NativeArray<float> RegionRoomTempDeltas;

    [ReadOnly] public NativeArray<int> FaceRegionA;
    [ReadOnly] public NativeArray<int> FaceRegionB;
    [ReadOnly] public NativeArray<float> FaceMaxPressureDeltaKpa;

    public NativeArray<float> RoomWeightedPressure;
    public NativeArray<float> RoomTotalVolume;
    public NativeArray<float> RoomWeightedTempDelta;
    public NativeArray<float> RoomMinPressureRating;
    public NativeArray<float> RoomMaxCurrentDeltaP;

    public int SimRegionCount;
    public int TotalFaceCount;
    public int RoomCount;
    public int SentinelRegionIndex;

    public void Execute()
    {
      // 1. Clear previous room data
      for (int i = 0; i < RoomCount; i++)
      {
        RoomWeightedPressure[i] = 0;
        RoomTotalVolume[i] = 0;
        RoomWeightedTempDelta[i] = 0;
        RoomMinPressureRating[i] = float.MaxValue;
        RoomMaxCurrentDeltaP[i] = 0;
      }

      // 2. Aggregate from all regions
      for (int rIdx = 0; rIdx < SentinelRegionIndex; rIdx++)
      {
        int roomIdx = RegionToRoomIndex[rIdx];

        if (roomIdx < 0 || roomIdx >= RoomCount) continue;

        float vol = RegionVolumes[rIdx];
        RoomWeightedPressure[roomIdx] += RegionPressureKpa[rIdx] * vol;
        RoomTotalVolume[roomIdx] += vol;
        RoomWeightedTempDelta[roomIdx] += RegionRoomTempDeltas[rIdx]; // This is already weighted in CalculateRoomTemperatureDeltas
      }

      // 3. Aggregate from all faces to find min pressure rating and max deltaP for each room boundary
      for (int fIdx = 0; fIdx < TotalFaceCount; fIdx++)
      {
        int rA = FaceRegionA[fIdx];
        int rB = FaceRegionB[fIdx];

        int roomA = (rA >= 0 && rA < SentinelRegionIndex) ? RegionToRoomIndex[rA] : -1;
        int roomB = (rB >= 0 && rB < SentinelRegionIndex) ? RegionToRoomIndex[rB] : -1;

        // If one side is sentinel, it's a room boundary
        if (rA == SentinelRegionIndex) roomA = -2; // sentinel
        if (rB == SentinelRegionIndex) roomB = -2;

        float rating = FaceMaxPressureDeltaKpa[fIdx];
        float pA = (rA >= 0 && rA < RegionPressureKpa.Length) ? RegionPressureKpa[rA] : 0f;
        float pB = (rB >= 0 && rB < RegionPressureKpa.Length) ? RegionPressureKpa[rB] : 0f;
        float deltaP = math.abs(pA - pB);

        // A face is a boundary for room A if room B is different AND NOT virtual
        if (roomA >= 0 && roomA < RoomCount && roomA != roomB && roomB != -1)
        {
          if (rating < RoomMinPressureRating[roomA]) RoomMinPressureRating[roomA] = rating;
          if (deltaP > RoomMaxCurrentDeltaP[roomA]) RoomMaxCurrentDeltaP[roomA] = deltaP;
        }

        // A face is a boundary for room B if room A is different AND NOT virtual
        if (roomB >= 0 && roomB < RoomCount && roomB != roomA && roomA != -1)
        {
          if (rating < RoomMinPressureRating[roomB]) RoomMinPressureRating[roomB] = rating;
          if (deltaP > RoomMaxCurrentDeltaP[roomB]) RoomMaxCurrentDeltaP[roomB] = deltaP;
        }
      }
    }
  }
}
