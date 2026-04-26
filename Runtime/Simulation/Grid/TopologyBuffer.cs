using SolarWeb.Pneuma.Data;
using System;
using Unity.Collections;
using Unity.Mathematics;

namespace SolarWeb.Pneuma.Grid
{
  public class TopologyBuffer : IDisposable
  {
    public NativeArray<float> CellThermalConductivity;
    public NativeArray<float> CellThermalCapacity;
    public NativeArray<float> CellStructuralConductance;
    public NativeArray<float> CellGasPermeability;
    public NativeArray<float> CellFlowArea;
    public NativeArray<float> CellMaxPressureDeltaKpa;
    public NativeArray<float> CellMinColDia;
    public NativeArray<float> CellMaxColDia;
    public NativeArray<float> CellPumpRate;
    public NativeArray<sbyte> CellFlowDirX;
    public NativeArray<sbyte> CellFlowDirZ;
    public NativeArray<byte> CellFlags; // bit0=isBarrier, bit2=hasBuilding, bit3=isSimulated

    public NativeArray<float> CellTopPermeability;
    public NativeArray<float> CellTopConductivity;
    public NativeArray<float> CellBottomPermeability;
    public NativeArray<float> CellBottomConductivity;

    public NativeArray<int> FaceLinkOffsets;
    public NativeArray<int> FaceLinkCounts;
    public NativeArray<int> FaceLinkWorldStart;
    public NativeArray<int3> FaceLinkDirection;

    public void Initialize(int worldCellCount, int stride, int faceStride, int linkCount)
    {
      CellThermalConductivity.Resize(worldCellCount);
      CellThermalCapacity.Resize(worldCellCount);
      CellStructuralConductance.Resize(worldCellCount);
      CellGasPermeability.Resize(worldCellCount);
      CellFlowArea.Resize(worldCellCount);
      CellMaxPressureDeltaKpa.Resize(worldCellCount);
      CellMinColDia.Resize(worldCellCount);
      CellMaxColDia.Resize(worldCellCount);
      CellPumpRate.Resize(worldCellCount);
      CellFlowDirX.Resize(worldCellCount);
      CellFlowDirZ.Resize(worldCellCount);
      CellFlags.Resize(worldCellCount);

      CellTopPermeability.Resize(worldCellCount);
      CellTopConductivity.Resize(worldCellCount);
      CellBottomPermeability.Resize(worldCellCount);
      CellBottomConductivity.Resize(worldCellCount);

      FaceLinkOffsets.Resize(faceStride);
      FaceLinkCounts.Resize(faceStride);
      FaceLinkWorldStart.Resize(linkCount);
      FaceLinkDirection.Resize(linkCount);
    }

    public void Dispose()
    {
      CellThermalConductivity.SafeDispose();
      CellThermalCapacity.SafeDispose();
      CellStructuralConductance.SafeDispose();
      CellGasPermeability.SafeDispose();
      CellFlowArea.SafeDispose();
      CellMaxPressureDeltaKpa.SafeDispose();
      CellMinColDia.SafeDispose();
      CellMaxColDia.SafeDispose();
      CellPumpRate.SafeDispose();
      CellFlowDirX.SafeDispose();
      CellFlowDirZ.SafeDispose();
      CellFlags.SafeDispose();

      CellTopPermeability.SafeDispose();
      CellTopConductivity.SafeDispose();
      CellBottomPermeability.SafeDispose();
      CellBottomConductivity.SafeDispose();

      FaceLinkOffsets.SafeDispose();
      FaceLinkCounts.SafeDispose();
      FaceLinkWorldStart.SafeDispose();
      FaceLinkDirection.SafeDispose();
    }
  }
}