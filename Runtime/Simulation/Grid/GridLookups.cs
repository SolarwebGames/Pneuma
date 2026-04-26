using System;
using SolarWeb.Pneuma.Data;
using Unity.Collections;
using Unity.Mathematics;

namespace SolarWeb.Pneuma.Grid
{
  public class GridLookups : IDisposable
  {
    public NativeArray<int> WorldToSimIndex;
    public NativeArray<int> SimToWorldIndex;

    public NativeArray<int> WorldToRegionIndex;

    // ── Static cell-face topology (built once, used by RegionGraphMutator) ──
    // Per-face (indexed 0..TotalCellFaceCount-1):
    public NativeArray<int> CellFaceCellA;
    public NativeArray<int> CellFaceCellB;
    public NativeArray<byte> CellFaceType;         // 0=Wall, 1=Roof, 2=Floor
    public NativeArray<float> CellFacePermeability;
    public NativeArray<float> CellFaceConductivity;
    public NativeArray<float> CellFaceSurfaceArea;
    public NativeArray<float> CellFaceMinCollisionDiameter;
    public NativeArray<float> CellFaceMaxCollisionDiameter;
    public NativeArray<int3> CellFaceDirection;

    public NativeArray<int> CellFaceOffsets;
    public NativeArray<int> CellFaceCounts;
    public NativeArray<int> CellFaceIndices;

    public void Initialize(int worldCellCount, int stride, int cellFaceCount, int adjSlots)
    {
      WorldToSimIndex.Resize(worldCellCount);
      SimToWorldIndex.Resize(stride);
      WorldToRegionIndex.Resize(worldCellCount);

      CellFaceCellA.Resize(cellFaceCount);
      CellFaceCellB.Resize(cellFaceCount);
      CellFaceType.Resize(cellFaceCount);
      CellFacePermeability.Resize(cellFaceCount);
      CellFaceConductivity.Resize(cellFaceCount);
      CellFaceSurfaceArea.Resize(cellFaceCount);
      CellFaceMinCollisionDiameter.Resize(cellFaceCount);
      CellFaceMaxCollisionDiameter.Resize(cellFaceCount);
      CellFaceDirection.Resize(cellFaceCount);

      CellFaceOffsets.Resize(stride);
      CellFaceCounts.Resize(stride);
      CellFaceIndices.Resize(adjSlots);
    }

    public void Dispose()
    {
      WorldToSimIndex.SafeDispose();
      SimToWorldIndex.SafeDispose();
      WorldToRegionIndex.SafeDispose();

      CellFaceCellA.SafeDispose();
      CellFaceCellB.SafeDispose();
      CellFaceType.SafeDispose();
      CellFacePermeability.SafeDispose();
      CellFaceConductivity.SafeDispose();
      CellFaceSurfaceArea.SafeDispose();
      CellFaceMinCollisionDiameter.SafeDispose();
      CellFaceMaxCollisionDiameter.SafeDispose();
      CellFaceDirection.SafeDispose();

      CellFaceOffsets.SafeDispose();
      CellFaceCounts.SafeDispose();
      CellFaceIndices.SafeDispose();
    }
  }
}
