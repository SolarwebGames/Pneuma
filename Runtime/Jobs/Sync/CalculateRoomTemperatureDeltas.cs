using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace SolarWeb.Pneuma.Jobs.Sync
{
  [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
  public struct CalculateRoomTemperatureDeltas : IJobParallelFor
  {
    [ReadOnly] public NativeArray<int> RegionToRoomIndex;
    [ReadOnly] public NativeArray<float> TemperatureK;
    [ReadOnly] public NativeArray<float> RegionVolumes;
    [ReadOnly] public NativeArray<float> RoomCurrentTemperatureK; // The temperature currently this room is at (in Kelvins)

    [WriteOnly] public NativeArray<float> RegionRoomTempDeltas;
    public NativeArray<int> ActiveTicks;

    public int SentinelRegionIndex;
    public int HysteresisTicks;

    public void Execute(int rIdx)
    {
      if (rIdx == SentinelRegionIndex || RegionToRoomIndex[rIdx] == -1)
      {
        RegionRoomTempDeltas[rIdx] = 0;
        return;
      }

      float currentRWT = RoomCurrentTemperatureK[rIdx];
      float newSimT = TemperatureK[rIdx];
      float deltaT = newSimT - currentRWT;

      if (math.abs(deltaT) < 1e-4f)
      {
        RegionRoomTempDeltas[rIdx] = 0;
        return;
      }

      ActiveTicks[rIdx] = HysteresisTicks;

      // We store the weighted delta. The interface will sum these up per room.
      RegionRoomTempDeltas[rIdx] = deltaT * RegionVolumes[rIdx];
    }
  }
}
