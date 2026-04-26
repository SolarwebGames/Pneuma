using Unity.Collections;
using Unity.Jobs;
using SolarWeb.Pneuma.Jobs.Environment;
using SolarWeb.Pneuma.Jobs.Shared;
using System;

namespace SolarWeb.Pneuma.Simulation.Environment
{
  public class WindMapManager : IDisposable
  {
    public NativeArray<float> CellWindExposure;
    public NativeArray<byte> BlocksWind;
    public NativeArray<byte> IsRoofed;

    public NativeList<int> DirtyCells;
    private NativeArray<byte> isCellDirty;

    public NativeList<int> DirtyFaces;
    private NativeArray<byte> isFaceDirty;

    private readonly AtmosphereManager manager;
    private bool fullRefresh = true;

    public WindMapManager(AtmosphereManager manager)
    {
      this.manager = manager;
    }

    public void Initialize()
    {
      int cellCount = manager.State.WorldCellCount;
      int faceCount = manager.State.RegionFaceBuffer.TotalFaceCount;

      if (CellWindExposure.IsCreated) CellWindExposure.Dispose();
      if (BlocksWind.IsCreated) BlocksWind.Dispose();
      if (IsRoofed.IsCreated) IsRoofed.Dispose();
      if (isCellDirty.IsCreated) isCellDirty.Dispose();
      if (isFaceDirty.IsCreated) isFaceDirty.Dispose();
      if (DirtyCells.IsCreated) DirtyCells.Dispose();
      if (DirtyFaces.IsCreated) DirtyFaces.Dispose();

      CellWindExposure = new NativeArray<float>(cellCount, Allocator.Persistent);
      BlocksWind = new NativeArray<byte>(cellCount, Allocator.Persistent);
      IsRoofed = new NativeArray<byte>(cellCount, Allocator.Persistent);
      DirtyCells = new NativeList<int>(1024, Allocator.Persistent);
      isCellDirty = new NativeArray<byte>(cellCount, Allocator.Persistent);
      DirtyFaces = new NativeList<int>(faceCount, Allocator.Persistent);
      isFaceDirty = new NativeArray<byte>(faceCount, Allocator.Persistent);
      fullRefresh = true;
    }

    public void MarkDirty() => fullRefresh = true;

    public void MarkDirty(int worldIdx)
    {
      if (fullRefresh) return;
      int width = manager.State.MapWidth;
      int height = manager.State.MapHeight;
      int x = worldIdx % width;
      int z = worldIdx / width;

      for (int dz = -10; dz <= 10; dz++)
      {
        for (int dx = -10; dx <= 10; dx++)
        {
          int nx = x + dx;
          int nz = z + dz;
          if (nx >= 0 && nx < width && nz >= 0 && nz < height)
          {
            int idx = nz * width + nx;
            if (isCellDirty[idx] == 0)
            {
              isCellDirty[idx] = 1;
              DirtyCells.Add(idx);
            }
          }
        }
      }
    }

    public JobHandle UpdateWindExposure(JobHandle dependency)
    {
      var state = manager.State;
      JobHandle handle = dependency;

      if (fullRefresh)
      {
        fullRefresh = false;
        DirtyCells.Clear();
        DirtyFaces.Clear();
        for (int i = 0; i < isCellDirty.Length; i++) isCellDirty[i] = 0;
        for (int i = 0; i < isFaceDirty.Length; i++) isFaceDirty[i] = 0;

        var fullCellsJob = new UpdateCellWindExposure
        {
          BlocksWind = BlocksWind,
          IsRoofed = IsRoofed,
          CellWindExposure = CellWindExposure,
          MapWidth = state.MapWidth,
          MapHeight = state.MapHeight
        };
        handle = fullCellsJob.Schedule(state.WorldCellCount, 64, dependency);

        var fullFacesJob = new UpdateFaceWindExposure
        {
          FaceRegionA = state.RegionFaceBuffer.FaceRegionA,
          FaceRegionB = state.RegionFaceBuffer.FaceRegionB,
          RegionFaceToCellOffsets = state.RegionFaceBuffer.RegionFaceToCellOffsets,
          RegionFaceToCellCounts = state.RegionFaceBuffer.RegionFaceToCellCounts,
          RegionFaceToCellSimA = state.RegionFaceBuffer.RegionFaceToCellSimA,
          RegionFaceToCellSimB = state.RegionFaceBuffer.RegionFaceToCellSimB,
          SimToWorldIndex = state.GridLookups.SimToWorldIndex,
          CellWindExposure = CellWindExposure,
          FaceDirection = state.RegionFaceBuffer.FaceDirection,
          FaceSurfaceArea = state.RegionFaceBuffer.FaceSurfaceArea,
          FaceWindExposureX = state.RegionFaceBuffer.FaceWindExposureX,
          FaceWindExposureZ = state.RegionFaceBuffer.FaceWindExposureZ,
          SentinelRegionIndex = state.SentinelRegionIndex
        };
        handle = fullFacesJob.Schedule(state.RegionFaceBuffer.TotalFaceCount, 64, handle);
        return handle;
      }

      if (DirtyCells.Length > 0)
      {
        var localCellsJob = new UpdateDirtyCells
        {
          DirtyIndices = DirtyCells.AsArray(),
          BlocksWind = BlocksWind,
          IsRoofed = IsRoofed,
          CellWindExposure = CellWindExposure,
          IsCellDirty = isCellDirty,
          MapWidth = state.MapWidth,
          MapHeight = state.MapHeight
        };
        handle = localCellsJob.Schedule(DirtyCells.Length, 32, dependency);

        var identifyFacesJob = new IdentifyAffectedFaces
        {
          DirtyCellIndices = DirtyCells.AsArray(),
          WorldToSimIndex = state.GridLookups.WorldToSimIndex,
          SentinelCellIndex = state.SentinelCellIndex,
          WorldToRegionIndex = state.GridLookups.WorldToRegionIndex,
          RegionFaceOffsets = state.RegionFaceBuffer.RegionFaceOffsets,
          RegionFaceCounts = state.RegionFaceBuffer.RegionFaceCounts,
          RegionFaceIndices = state.RegionFaceBuffer.RegionFaceIndices,
          IsFaceDirty = isFaceDirty,
          DirtyFaces = DirtyFaces.AsParallelWriter()
        };
        handle = identifyFacesJob.Schedule(DirtyCells.Length, 32, handle);

        // Clear the DirtyCells after processing
        var clearCellsJob = new ClearList { List = DirtyCells };
        handle = clearCellsJob.Schedule(handle);

        // Now schedule the face update for identified faces
        var updateDirtyFacesJob = new UpdateDirtyFaceWindExposure
        {
          DirtyFaceIndices = DirtyFaces.AsDeferredJobArray(),
          RegionFaceToCellOffsets = state.RegionFaceBuffer.RegionFaceToCellOffsets,
          RegionFaceToCellCounts = state.RegionFaceBuffer.RegionFaceToCellCounts,
          RegionFaceToCellSimA = state.RegionFaceBuffer.RegionFaceToCellSimA,
          SimToWorldIndex = state.GridLookups.SimToWorldIndex,
          CellWindExposure = CellWindExposure,
          FaceDirection = state.RegionFaceBuffer.FaceDirection,
          FaceSurfaceArea = state.RegionFaceBuffer.FaceSurfaceArea,
          FaceWindExposureX = state.RegionFaceBuffer.FaceWindExposureX,
          FaceWindExposureZ = state.RegionFaceBuffer.FaceWindExposureZ,
          IsFaceDirty = isFaceDirty
        };
        handle = updateDirtyFacesJob.Schedule(DirtyFaces, 32, handle);

        // Clear the DirtyFaces after processing
        var clearFacesJob = new ClearList { List = DirtyFaces };
        handle = clearFacesJob.Schedule(handle);
      }

      return handle;
    }

    public void Dispose()
    {
      if (CellWindExposure.IsCreated) CellWindExposure.Dispose();
      if (BlocksWind.IsCreated) BlocksWind.Dispose();
      if (IsRoofed.IsCreated) IsRoofed.Dispose();
      if (DirtyCells.IsCreated) DirtyCells.Dispose();
      if (isCellDirty.IsCreated) isCellDirty.Dispose();
      if (DirtyFaces.IsCreated) DirtyFaces.Dispose();
      if (isFaceDirty.IsCreated) isFaceDirty.Dispose();
    }
  }
}
