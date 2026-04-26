using System;
using SolarWeb.Pneuma.Data;
using Unity.Collections;

namespace SolarWeb.Pneuma.Grid
{
  public class RegionStates : IDisposable
  {
    public NativeArray<bool> IsActive;
    public NativeArray<bool> IsBurning;
    public NativeArray<float> BurnIntensity;
    public NativeArray<int> ActiveTicks;
    public NativeArray<int> EquilibriumTicks;
    public NativeList<int> ActiveIndices;
    public NativeList<int> ActiveCellIndices;
    public NativeArray<int> RegionToRoomID;
    public NativeArray<int> RegionToRoomIndex; // Dense index for Room aggregation [0..NumRooms-1]
    public NativeArray<float> RegionRoomTempDeltas;

    // --- Room Aggregation Buffers (Aggregated from regions in a job) ---
    public NativeArray<float> RoomWeightedPressure;
    public NativeArray<float> RoomTotalVolume;
    public NativeArray<float> RoomWeightedTempDelta;
    public NativeArray<float> RoomMinPressureRating;
    public NativeArray<float> RoomMaxCurrentDeltaP;
    public int RoomCount;

    // --- Region Boundary Tracking (used for rebuilding grid state) ---
    public NativeArray<int> MinX;
    public NativeArray<int> MinZ;
    public NativeArray<int> MaxX;
    public NativeArray<int> MaxZ;

    public void Initialize(int regionStride, int cellStride)
    {
      IsActive.Resize(regionStride);
      IsBurning.Resize(regionStride);
      BurnIntensity.Resize(regionStride);
      ActiveTicks.Resize(regionStride);
      EquilibriumTicks.Resize(regionStride);
      ActiveIndices = new NativeList<int>(regionStride, Allocator.Persistent);
      ActiveCellIndices = new NativeList<int>(cellStride, Allocator.Persistent);
      RegionToRoomID.Resize(regionStride);
      RegionToRoomID.Fill(-1);
      RegionToRoomIndex.Resize(regionStride);
      RegionToRoomIndex.Fill(-1);
      RegionRoomTempDeltas.Resize(regionStride);

      RoomWeightedPressure.Resize(512);
      RoomTotalVolume.Resize(512);
      RoomWeightedTempDelta.Resize(512);
      RoomMinPressureRating.Resize(512);
      RoomMaxCurrentDeltaP.Resize(512);

      MinX.Resize(regionStride);
      MinZ.Resize(regionStride);
      MaxX.Resize(regionStride);
      MaxZ.Resize(regionStride);
    }

    public void Dispose()
    {
      IsActive.SafeDispose();
      IsBurning.SafeDispose();
      BurnIntensity.SafeDispose();
      ActiveTicks.SafeDispose();
      EquilibriumTicks.SafeDispose();
      ActiveIndices.SafeDispose();
      ActiveCellIndices.SafeDispose();
      RegionToRoomID.SafeDispose();
      RegionToRoomIndex.SafeDispose();
      RegionRoomTempDeltas.SafeDispose();

      RoomWeightedPressure.SafeDispose();
      RoomTotalVolume.SafeDispose();
      RoomWeightedTempDelta.SafeDispose();
      RoomMinPressureRating.SafeDispose();
      RoomMaxCurrentDeltaP.SafeDispose();

      MinX.SafeDispose();
      MinZ.SafeDispose();
      MaxX.SafeDispose();
      MaxZ.SafeDispose();
    }
  }
}