using SolarWeb.Pneuma.Data;
using System;
using Unity.Collections;
using Unity.Mathematics;

namespace SolarWeb.Pneuma.Grid
{
  public class RegionFaceBuffer : IDisposable
  {
    public int TotalFaceCount;
    public int FaceStride;

    public NativeArray<int> FaceRegionA;
    public NativeArray<int> FaceRegionB;
    public NativeArray<byte> FaceType;

    public NativeArray<int> RegionFaceOffsets;
    public NativeArray<int> RegionFaceCounts;
    public NativeArray<int> RegionFaceIndices;

    public NativeArray<float> FaceGasPermeability;
    public NativeArray<float> FaceThermalConductivity;
    public NativeArray<float> FaceSurfaceArea;
    public NativeArray<float> FaceLimitedVel;
    public NativeArray<float> FaceMaxSafeAdvection;
    public NativeArray<float> FacePressureOffsetKpa;
    public NativeArray<float> FaceTFactor;

    public NativeArray<float> MinCollisionDiameter;
    public NativeArray<float> MaxCollisionDiameter;

    public NativeArray<sbyte> FaceFlowDirection;
    public NativeArray<float> FaceActivePumpRate;
    public NativeArray<float> FaceMaxPumpPressureKpa;
    public NativeArray<float> FaceRegulatorKpa;
    public NativeArray<float> FaceWindExposureX;
    public NativeArray<float> FaceWindExposureZ;
    public NativeArray<int3> FaceDirection;
    public NativeArray<float> FaceMaxPressureDeltaKpa;

    public NativeArray<float> FaceGasFlux;
    public NativeArray<float> FaceTotalGasFlux;
    public NativeArray<float> FaceThermalFlux;
    public NativeArray<float> EnclosingFaceThermalFlux; // J per tick, struct-to-struct across face

    /// <summary>
    /// Per-region-face offset into RegionFaceToCellSimA/B flat arrays.
    /// Size: FaceStride.
    /// </summary>
    public NativeArray<int> RegionFaceToCellOffsets;

    /// <summary>Number of constituent sim-cell pairs for each region face. Size: FaceStride.</summary>
    public NativeArray<int> RegionFaceToCellCounts;

    /// <summary>
    /// Flat list of sim-cell A indices for each region-face constituent pair.
    /// Total size is sum of all RegionFaceToCellCounts.
    /// </summary>
    public NativeArray<int> RegionFaceToCellSimA;

    /// <summary>Flat list of sim-cell B indices (parallel to RegionFaceToCellSimA).</summary>
    public NativeArray<int> RegionFaceToCellSimB;

    public void Initialize(int faceCount, int faceStride, int gasCount, int regionStride, int totalCellPairCount)
    {
      TotalFaceCount = faceCount;
      FaceStride = faceStride;

      FaceRegionA.Resize(faceStride);
      FaceRegionB.Resize(faceStride);
      FaceType.Resize(faceStride);
      FaceGasPermeability.Resize(faceStride);
      FaceThermalConductivity.Resize(faceStride);
      FaceSurfaceArea.Resize(faceStride);

      MinCollisionDiameter.Resize(faceStride);
      MaxCollisionDiameter.Resize(faceStride);
      FaceLimitedVel.Resize(faceStride);
      FaceMaxSafeAdvection.Resize(faceStride);
      FaceFlowDirection.Resize(faceStride);
      FaceActivePumpRate.Resize(faceStride);
      FaceMaxPumpPressureKpa.Resize(faceStride);
      FaceRegulatorKpa.Resize(faceStride);
      FaceMaxPressureDeltaKpa.Resize(faceStride);
      FaceWindExposureX.Resize(faceStride);
      FaceWindExposureZ.Resize(faceStride);
      FaceDirection.Resize(faceStride);
      FacePressureOffsetKpa.Resize(faceStride);
      FaceTFactor.Resize(faceStride);
      FaceGasFlux.Resize(faceStride * gasCount);
      FaceTotalGasFlux.Resize(faceStride);
      FaceThermalFlux.Resize(faceStride);
      EnclosingFaceThermalFlux.Resize(faceStride);

      RegionFaceOffsets.Resize(regionStride);
      RegionFaceCounts.Resize(regionStride);
      RegionFaceIndices.Resize(faceCount * 2);

      RegionFaceToCellOffsets.Resize(faceStride);
      RegionFaceToCellCounts.Resize(faceStride);
      int pairSlots = System.Math.Max(1, totalCellPairCount);
      RegionFaceToCellSimA.Resize(pairSlots);
      RegionFaceToCellSimB.Resize(pairSlots);
    }

    public void Dispose()
    {
      FaceRegionA.SafeDispose();
      FaceRegionB.SafeDispose();
      FaceType.SafeDispose();
      FaceGasPermeability.SafeDispose();
      FaceThermalConductivity.SafeDispose();
      FaceSurfaceArea.SafeDispose();
      MinCollisionDiameter.SafeDispose();
      MaxCollisionDiameter.SafeDispose();
      FaceLimitedVel.SafeDispose();
      FaceMaxSafeAdvection.SafeDispose();
      FaceFlowDirection.SafeDispose();
      FaceActivePumpRate.SafeDispose();
      FaceMaxPumpPressureKpa.SafeDispose();
      FaceRegulatorKpa.SafeDispose();
      FaceMaxPressureDeltaKpa.SafeDispose();
      FaceWindExposureX.SafeDispose();
      FaceWindExposureZ.SafeDispose();
      FaceDirection.SafeDispose();
      FacePressureOffsetKpa.SafeDispose();
      FaceTFactor.SafeDispose();
      FaceGasFlux.SafeDispose();
      FaceTotalGasFlux.SafeDispose();
      FaceThermalFlux.SafeDispose();
      EnclosingFaceThermalFlux.SafeDispose();
      RegionFaceOffsets.SafeDispose();
      RegionFaceCounts.SafeDispose();
      RegionFaceIndices.SafeDispose();
      RegionFaceToCellOffsets.SafeDispose();
      RegionFaceToCellCounts.SafeDispose();
      RegionFaceToCellSimA.SafeDispose();
      RegionFaceToCellSimB.SafeDispose();
    }
  }
}