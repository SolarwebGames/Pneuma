using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace SolarWeb.Pneuma.Jobs.Diffusion
{
  /// <summary>
  /// Recomputes face metrics (permeability, conductivity, etc.) for a set of dirty region
  /// faces using the per-cell physics cache in TopologyBuffer.  Mirrors the logic of
  /// FaceScanner.CalculateLinkMetrics but runs in Burst without any managed allocations.
  ///
  /// CellFlags bit layout (set by TopologyMapper.ComputeCellTopology):
  ///   bit0 = isBarrier       – building has near-zero permeability
  ///   bit1 = (Reserved)
  ///   bit2 = hasBuilding     – at least one structural building (edifice) occupies the cell
  ///   bit3 = isSimulated     – cell belongs to a tracked region; link walk must stop here
  ///   bit4 = isStandable     – pawns can stand in this cell (furniture is usually standable)
  ///   bit5 = isOutdoors      – cell belongs to a room that uses outdoor temperature
  /// </summary>
  [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
  public struct RecomputeDirtyFaceMetrics : IJobParallelFor
  {
    [ReadOnly] public NativeArray<int> DirtyFaceIndices;

    [ReadOnly] public NativeArray<float> CellThermalConductivity;
    [ReadOnly] public NativeArray<float> CellGasPermeability;
    [ReadOnly] public NativeArray<float> CellMaxPressureDeltaKpa;
    [ReadOnly] public NativeArray<float> CellMinColDia;
    [ReadOnly] public NativeArray<float> CellMaxColDia;
    [ReadOnly] public NativeArray<float> CellPumpRate;
    [ReadOnly] public NativeArray<sbyte> CellFlowDirX;
    [ReadOnly] public NativeArray<sbyte> CellFlowDirZ;
    [ReadOnly] public NativeArray<byte> CellFlags;

    [ReadOnly] public NativeArray<float> CellTopPermeability;
    [ReadOnly] public NativeArray<float> CellTopConductivity;
    [ReadOnly] public NativeArray<float> CellBottomPermeability;
    [ReadOnly] public NativeArray<float> CellBottomConductivity;

    [ReadOnly] public NativeArray<int> WorldToRegionIndex;
    [ReadOnly] public NativeArray<int> SimToWorldIndex;
    [ReadOnly] public NativeArray<int> FaceLinkOffsets;
    [ReadOnly] public NativeArray<int> FaceLinkCounts;
    [ReadOnly] public NativeArray<int> FaceLinkWorldStart;
    [ReadOnly] public NativeArray<int3> FaceLinkDirection;

    [ReadOnly] public NativeArray<byte> FaceType;
    [ReadOnly] public NativeArray<int> RegionFaceToCellOffsets;
    [ReadOnly] public NativeArray<int> RegionFaceToCellCounts;
    [ReadOnly] public NativeArray<int> RegionFaceToCellSimA;

    public int MapWidth;
    public int MapHeight;
    public int SentinelRegionIndex;

    // RegionFaceBuffer write targets — each Execute touches a unique faceIdx
    [NativeDisableParallelForRestriction] public NativeArray<float> FaceGasPermeability;
    [NativeDisableParallelForRestriction] public NativeArray<float> FaceThermalConductivity;
    [NativeDisableParallelForRestriction] public NativeArray<float> FaceMaxPressureDeltaKpa;
    [NativeDisableParallelForRestriction] public NativeArray<float> MinCollisionDiameter;
    [NativeDisableParallelForRestriction] public NativeArray<float> MaxCollisionDiameter;
    [NativeDisableParallelForRestriction] public NativeArray<float> FaceActivePumpRate;
    [NativeDisableParallelForRestriction] public NativeArray<sbyte> FaceFlowDirection;

    public void Execute(int i)
    {
      int faceIdx = DirtyFaceIndices[i];
      byte fType = FaceType[faceIdx];

      if (fType == 1 || fType == 2)
      {
        int pairOffset = RegionFaceToCellOffsets[faceIdx];
        int pairCount = RegionFaceToCellCounts[faceIdx];

        float totalP = 0f;
        float totalK = 0f;

        for (int p = 0; p < pairCount; p++)
        {
          int simA = RegionFaceToCellSimA[pairOffset + p];
          int worldIdx = SimToWorldIndex[simA];

          if (fType == 1)
          {
            totalP += CellTopPermeability[worldIdx];
            totalK += CellTopConductivity[worldIdx];
          }
          else
          {
            totalP += CellBottomPermeability[worldIdx];
            totalK += CellBottomConductivity[worldIdx];
          }
        }

        FaceGasPermeability[faceIdx] = totalP;
        FaceThermalConductivity[faceIdx] = totalK;
        FaceMaxPressureDeltaKpa[faceIdx] = float.MaxValue;
        MinCollisionDiameter[faceIdx] = 0f;
        MaxCollisionDiameter[faceIdx] = float.MaxValue;
        FaceActivePumpRate[faceIdx] = 0f;
        FaceFlowDirection[faceIdx] = 0;
        return;
      }

      int linkOffset = FaceLinkOffsets[faceIdx];
      int linkCount = FaceLinkCounts[faceIdx];

      if (linkCount == 0) return;

      float totalGasConductance = 0f;
      float totalThermalConductance = 0f;
      float minColDia = 0f;
      float maxColDia = float.MaxValue;
      float finalPumpRate = 0f;
      sbyte finalFlowDir = 0;
      float minMaxPressure = float.MaxValue;

      for (int l = 0; l < linkCount; l++)
      {
        int linkIdx = linkOffset + l;
        int startWorldIdx = FaceLinkWorldStart[linkIdx];
        int3 dir = FaceLinkDirection[linkIdx];

        float linkGasR = 0.5f / (1.0f * 2.5f);
        float linkThermR = 0.5f / (0.065f * 2.5f);
        float linkMaxP = 0f;
        float linkMinColDia = 0f;
        float linkMaxColDia = float.MaxValue;
        float linkPumpRate = 0f;
        sbyte linkFlowDir = 0;
        bool hitBarrier = false;

        int curX = startWorldIdx % MapWidth;
        int curZ = startWorldIdx / MapWidth;

        while (true)
        {
          curX += dir.x;
          curZ += dir.z;

          if (curX < 0 || curX >= MapWidth || curZ < 0 || curZ >= MapHeight)
          {
            linkGasR += 0.5f / (1.0f * 2.5f);
            linkThermR += 0.5f / (0.065f * 2.5f);
            if (!hitBarrier) linkMaxP = float.MaxValue;
            break;
          }

          int currentWorldIdx = curZ * MapWidth + curX;

          byte flags = CellFlags[currentWorldIdx];

          if ((flags & 8) != 0 || (flags & 32) != 0)
          {
            linkGasR += 0.5f / (1.0f * 2.5f);
            linkThermR += 0.5f / (0.065f * 2.5f);
            if (!hitBarrier) linkMaxP = float.MaxValue;
            break;
          }

          float cellPerm = CellGasPermeability[currentWorldIdx];
          float cellCond = CellThermalConductivity[currentWorldIdx];

          linkGasR += 1.0f / (cellPerm * 2.5f);
          linkThermR += 1.0f / (math.max(0.0001f, cellCond) * 2.5f);
          linkMaxP += CellMaxPressureDeltaKpa[currentWorldIdx];
          hitBarrier = true;

          linkMinColDia = math.max(linkMinColDia, CellMinColDia[currentWorldIdx]);
          linkMaxColDia = math.min(linkMaxColDia, CellMaxColDia[currentWorldIdx]);

          // Pump rate and flow direction are evaluated via stored direction components,
          // as a disabled pump still has an orientation.
          sbyte fdirX = CellFlowDirX[currentWorldIdx];
          sbyte fdirZ = CellFlowDirZ[currentWorldIdx];
          if (fdirX != 0 || fdirZ != 0)
          {
            linkPumpRate = CellPumpRate[currentWorldIdx];
            int dot = dir.x * fdirX + dir.z * fdirZ;
            if (dot > 0) linkFlowDir = 1;
            else if (dot < 0) linkFlowDir = -1;
          }
        }

        totalGasConductance += 1.0f / math.max(0.0001f, linkGasR);
        totalThermalConductance += 1.0f / math.max(0.0001f, linkThermR);

        if (linkMaxP < minMaxPressure) minMaxPressure = linkMaxP;

        minColDia = math.max(minColDia, linkMinColDia);
        maxColDia = math.min(maxColDia, linkMaxColDia);

        finalPumpRate += linkPumpRate;
        if (linkFlowDir != 0) finalFlowDir = linkFlowDir;
      }

      FaceGasPermeability[faceIdx] = totalGasConductance;
      FaceThermalConductivity[faceIdx] = totalThermalConductance;
      FaceMaxPressureDeltaKpa[faceIdx] = minMaxPressure;
      MinCollisionDiameter[faceIdx] = minColDia;
      MaxCollisionDiameter[faceIdx] = maxColDia;
      FaceActivePumpRate[faceIdx] = finalPumpRate;
      FaceFlowDirection[faceIdx] = finalFlowDir;
    }
  }
}