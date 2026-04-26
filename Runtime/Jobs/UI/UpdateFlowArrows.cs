using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

using SolarWeb.Pneuma.Data;

namespace SolarWeb.Pneuma.Jobs.UI
{
  [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
  public struct UpdateFlowArrows : IJob
  {
    [ReadOnly] public NativeArray<int> FaceRegionA;
    [ReadOnly] public NativeArray<int> FaceRegionB;
    [ReadOnly] public NativeArray<byte> FaceType;
    [ReadOnly] public NativeArray<float> FaceGasPermeability;
    [ReadOnly] public NativeArray<float> FaceGasFlux; // Stride * GasCount
    [ReadOnly] public NativeArray<int> RegionFaceToCellOffsets;
    [ReadOnly] public NativeArray<int> RegionFaceToCellCounts;
    [ReadOnly] public NativeArray<int> RegionFaceToCellSimA;
    [ReadOnly] public NativeArray<int> RegionFaceToCellSimB;
    [ReadOnly] public NativeArray<int3> FaceDirection;
    [ReadOnly] public NativeArray<int> SimToWorldIndex;
    [ReadOnly] public NativeArray<int> WorldToSimIndex;

    [ReadOnly] public NativeArray<float3> GasOverlayColor;
    [ReadOnly] public NativeArray<float> GasMolarMass;

    [ReadOnly] public NativeArray<int> DynFaceRegionA;
    [ReadOnly] public NativeArray<int> DynFaceRegionB;
    [ReadOnly] public NativeArray<byte> DynFaceType;
    [ReadOnly] public NativeArray<float> DynFaceGasPermeability;
    [ReadOnly] public NativeArray<float> DynFaceGasFlux;
    [ReadOnly] public NativeArray<int> DynFaceSourceEdge;
    [ReadOnly] public NativeArray<int3> CellFaceDirection;
    [ReadOnly] public NativeArray<int> CellFaceCellA;
    [ReadOnly] public NativeArray<int> CellFaceCellB;

    [ReadOnly] public NativeArray<float> RegionVolumes;

    public NativeList<FlowArrowData> OutArrows;

    public int FaceStride;
    public int GasCount;
    public int MapWidth;
    public int MapHeight;
    public int SentinelRegionIndex;
    public int SentinelCellIndex;
    public float VisibilityThresholdUMol;
    public float MinPermeabilityThreshold;

    public void Execute()
    {
      // 1. Static Faces
      for (int fIdx = 0; fIdx < RegionFaceToCellOffsets.Length; fIdx++)
      {
        if (FaceType[fIdx] != 0) continue;
        if (FaceGasPermeability[fIdx] < MinPermeabilityThreshold) continue;

        float netFlux = 0f;
        float maxAbsFlux = -1f;
        int maxGasIdx = 0;

        for (int g = 0; g < GasCount; g++)
        {
          float flux = FaceGasFlux[g * FaceStride + fIdx];
          netFlux += flux;
          float absFlux = math.abs(flux);
          if (absFlux > maxAbsFlux)
          {
            maxAbsFlux = absFlux;
            maxGasIdx = g;
          }
        }

        if (math.abs(netFlux) < VisibilityThresholdUMol) continue;

        float3 color = (maxAbsFlux > 0) ? GasOverlayColor[maxGasIdx] : new float3(1, 1, 1);
        float molarMass = (maxAbsFlux > 0) ? GasMolarMass[maxGasIdx] : 29f;
        ProcessFace(fIdx, netFlux, color, molarMass, false, FaceRegionA[fIdx], FaceRegionB[fIdx]);
      }

      // 2. Dynamic Faces
      for (int fIdx = 0; fIdx < DynFaceRegionA.Length; fIdx++)
      {
        if (DynFaceRegionA[fIdx] == SentinelRegionIndex) continue;
        if (DynFaceType[fIdx] != 0) continue;
        if (DynFaceGasPermeability[fIdx] < MinPermeabilityThreshold) continue;

        float netFlux = 0f;
        float maxAbsFlux = -1f;
        int maxGasIdx = 0;

        for (int g = 0; g < GasCount; g++)
        {
          float flux = DynFaceGasFlux[g * DynFaceRegionA.Length + fIdx];
          netFlux += flux;
          float absFlux = math.abs(flux);
          if (absFlux > maxAbsFlux)
          {
            maxAbsFlux = absFlux;
            maxGasIdx = g;
          }
        }

        if (math.abs(netFlux) < VisibilityThresholdUMol) continue;

        float3 color = (maxAbsFlux > 0) ? GasOverlayColor[maxGasIdx] : new float3(1, 1, 1);
        float molarMass = (maxAbsFlux > 0) ? GasMolarMass[maxGasIdx] : 29f;
        ProcessFace(fIdx, netFlux, color, molarMass, true, DynFaceRegionA[fIdx], DynFaceRegionB[fIdx]);
      }
    }

    private void ProcessFace(int fIdx, float netFlux, float3 color, float molarMass, bool isDynamic, int regA, int regB)
    {
      float3 centerPos = float3.zero;
      float3 direction = float3.zero;
      int pairCount = 0;

      if (isDynamic)
      {
        int sourceEdge = DynFaceSourceEdge[fIdx];
        if (sourceEdge == -1) return;

        int simA = CellFaceCellA[sourceEdge];
        int simB = CellFaceCellB[sourceEdge];
        int3 dir = CellFaceDirection[sourceEdge];

        float3 posA = GetWorldPos(simA);
        float3 posB = posA + new float3(dir.x, dir.y, dir.z);

        centerPos = (posA + posB) * 0.5f;
        direction = new float3(dir.x, 0, dir.z);
        pairCount = 1;
      }
      else
      {
        int offset = RegionFaceToCellOffsets[fIdx];
        int count = RegionFaceToCellCounts[fIdx];
        int3 dir = FaceDirection[fIdx];
        float3 dirF = new float3(dir.x, dir.y, dir.z);

        for (int i = 0; i < count; i++)
        {
          int simA = RegionFaceToCellSimA[offset + i];
          float3 posA = GetWorldPos(simA);
          float3 posB = posA + dirF;

          centerPos += (posA + posB) * 0.5f;
          direction += new float3(dir.x, 0, dir.z);
          pairCount++;
        }

        if (pairCount > 0)
        {
          centerPos /= pairCount;
          direction /= pairCount;
        }
      }

      if (pairCount > 0 && math.lengthsq(direction.xz) > 0.001f)
      {
        float3 normDir = math.normalize(new float3(direction.x, 0, direction.z));
        if (netFlux < 0) normDir = -normDir;

        float volA = (regA == SentinelRegionIndex) ? 1e6f : RegionVolumes[regA];
        float volB = (regB == SentinelRegionIndex) ? 1e6f : RegionVolumes[regB];

        OutArrows.Add(new FlowArrowData
        {
          Center = centerPos,
          Direction = normDir,
          NetFlux = math.abs(netFlux),
          Color = color,
          MolarMass = molarMass,
          Length = pairCount,
          VolumeA = volA,
          VolumeB = volB
        });
      }
    }

    private float3 GetWorldPos(int simIdx)
    {
      int worldIdx = SimToWorldIndex[simIdx];
      return new float3((worldIdx % MapWidth) + 0.5f, 0, (worldIdx / MapWidth) + 0.5f);
    }
  }
}
