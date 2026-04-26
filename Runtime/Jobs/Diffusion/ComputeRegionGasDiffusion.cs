using SolarWeb.Pneuma.Logging;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace SolarWeb.Pneuma.Jobs.Diffusion
{
  [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
  public struct ComputeRegionGasDiffusion : IJobParallelFor
  {
    [ReadOnly, NoAlias] public NativeArray<float> MolarFractions;
    [WriteOnly, NoAlias] public NativeArray<float> FaceGasFlux;

    [ReadOnly] public NativeArray<int> FaceRegionA;
    [ReadOnly] public NativeArray<int> FaceRegionB;
    [ReadOnly] public NativeArray<float> FaceLimitedVel;
    [ReadOnly] public NativeArray<float> FaceMaxSafeAdvection;
    [ReadOnly] public NativeArray<float> FaceTFactor;
    [ReadOnly] public NativeArray<float> FaceGasPermeability;
    [ReadOnly] public NativeArray<float> FaceSurfaceArea;
    [ReadOnly] public NativeArray<float> S_Constants;
    [ReadOnly] public NativeArray<float> PressureKpa;
    [ReadOnly] public NativeArray<float> TemperatureK;
    [ReadOnly] public NativeArray<float> RegionInvVolConst;

    [ReadOnly] public NativeArray<float> GasCollisionDiameters;
    [ReadOnly] public NativeArray<float> MinCollisionDiameter;
    [ReadOnly] public NativeArray<float> MaxCollisionDiameter;
    [ReadOnly] public NativeArray<sbyte> FaceFlowDirection;
    [ReadOnly] public NativeArray<float> FaceActivePumpRate;
    [ReadOnly] public NativeArray<float> FaceMaxPumpPressureKpa;
    [ReadOnly] public NativeArray<float> FaceRegulatorKpa;

    [ReadOnly] public NativeArray<float> FaceWindExposureX;
    [ReadOnly] public NativeArray<float> FaceWindExposureZ;
    public float2 WindVelocity;

    [ReadOnly] public NativeArray<long> TotalUMoles;
    [ReadOnly] public NativeArray<int> RegionFaceCounts;
    [ReadOnly] public NativeArray<int> DynRegionFaceCount;
    public int DynamicRegionPoolStart;
    public int SentinelRegionIndex;

    public int RegionStride;
    public int FaceStride;
    public int FaceCount;
    public float TimeStep;

    public void Execute(int g)
    {
      float sConstant = S_Constants[g];
      float gasDiameter = GasCollisionDiameters[g];

      int gasOffset = g * RegionStride;
      int fluxOffset = g * FaceStride;

      float combinedScale = TimeStep * 1_000_000f;
      float scaledS = sConstant * combinedScale;

      for (int fIdx = 0; fIdx < FaceCount; fIdx++)
      {
        int iA = FaceRegionA[fIdx];
        int iB = FaceRegionB[fIdx];

        if (iA == SentinelRegionIndex && iB == SentinelRegionIndex)
        {
          FaceGasFlux[fluxOffset + fIdx] = 0f;
          continue;
        }

        bool canPass = (gasDiameter >= MinCollisionDiameter[fIdx]) & (gasDiameter <= MaxCollisionDiameter[fIdx]);
        float mask = math.select(0f, 1f, canPass);

        float fracA = MolarFractions[gasOffset + iA];
        float fracB = MolarFractions[gasOffset + iB];

        float pumpRate = FaceActivePumpRate[fIdx];
        sbyte flowDir = FaceFlowDirection[fIdx];

        float vel = FaceLimitedVel[fIdx];
        float windVel = (WindVelocity.x * FaceWindExposureX[fIdx] + WindVelocity.y * FaceWindExposureZ[fIdx]);
        float combinedVel = vel + windVel;

        if (flowDir != 0)
        {
          combinedVel = math.select(math.min(0f, combinedVel), math.max(0f, combinedVel), flowDir > 0);
        }

        float advectedFrac = math.select(fracB, fracA, combinedVel > 0);
        float advectionRate = combinedVel * advectedFrac * combinedScale;

        float diffusionRate = FaceGasPermeability[fIdx] * (fracA - fracB) * FaceTFactor[fIdx] * scaledS;

        if (flowDir != 0)
        {
          diffusionRate = math.select(math.min(0f, diffusionRate), math.max(0f, diffusionRate), flowDir > 0);
        }

        // Gradient-proportional equilibration limit: prevents per-tick gradient reversal (explicit stability).
        // Limits diffusion to at most |Δfrac| × min(nA,nB) × 0.25 / faceCount, ensuring the
        // composition gradient cannot be reversed in a single tick (the explicit stability criterion).
        int srcIdx = (diffusionRate > 0) ? iA : iB;
        int srcFaceCount = GetFaceCount(srcIdx);
        float nA = (float)TotalUMoles[iA];
        float nB = (float)TotalUMoles[iB];
        float gradEquilLimit = math.abs(fracA - fracB) * math.min(nA, nB) * 0.25f / (float)srcFaceCount;
        diffusionRate = math.clamp(diffusionRate, -gradEquilLimit, gradEquilLimit);

        // Back-pressure: reduce pump rate as destination pressure rises above source.
        float maxPumpPressure = FaceMaxPumpPressureKpa[fIdx];
        if (pumpRate != 0f && maxPumpPressure > 0f)
        {
          float pA = PressureKpa[iA];
          float pB = PressureKpa[iB];
          float backPressure = math.select(
            math.max(0f, pA - pB),
            math.max(0f, pB - pA),
            flowDir > 0);
          float pumpScale = math.max(0f, 1f - backPressure / maxPumpPressure);
          pumpRate *= pumpScale;
        }

        float pumpedFrac = math.select(fracB, fracA, flowDir > 0);
        float pumpFlux = (float)flowDir * pumpRate * pumpedFrac * TimeStep;

        float advectionPlusPump = advectionRate + pumpFlux;
        float limit = math.abs(FaceMaxSafeAdvection[fIdx] * 1_000_000f);
        float sourceFrac = math.select(fracB, fracA, advectionPlusPump > 0);
        float perGasLimit = limit * sourceFrac;
        float limitedAdvection = math.clamp(advectionPlusPump, -perGasLimit, perGasLimit);

        float fluxVal = limitedAdvection + diffusionRate;

        int netSrcIdx = (fluxVal > 0) ? iA : iB;
        float netSrcTotal = (float)TotalUMoles[netSrcIdx];
        float netSrcFrac = (fluxVal > 0) ? fracA : fracB;
        int netFaceCount = GetFaceCount(netSrcIdx);
        float safetyFactor = (flowDir != 0) ? 0.9f : 0.5f;
        float combinedLimit = (netSrcTotal * netSrcFrac * safetyFactor) / netFaceCount;
        fluxVal = math.clamp(fluxVal, -combinedLimit, combinedLimit);

        if (flowDir != 0)
        {
          fluxVal = math.select(math.min(0f, fluxVal), math.max(0f, fluxVal), flowDir > 0);
        }

        float maxRegP = FaceRegulatorKpa[fIdx];
        if (maxRegP >= 0f && fluxVal != 0f)
        {
          int iDest = (fluxVal > 0f) ? iB : iA;
          float pDest = PressureKpa[iDest];

          if (pDest >= maxRegP)
          {
            fluxVal = 0f;
          }
          else
          {
            float denomDest = RegionInvVolConst[iDest] * TemperatureK[iDest];
            if (denomDest > 1e-6f)
            {
              float molesToLimit = (maxRegP - pDest) * 1000f / denomDest;
              float uMolesToLimit = molesToLimit * 1_000_000f;
              float srcFrac = (fluxVal > 0f) ? fracA : fracB;

              float faceLimit = (uMolesToLimit * 0.25f * srcFrac) / (float)GetFaceCount(iDest);
              fluxVal = (fluxVal > 0f)
                  ? math.min(fluxVal, math.max(0f, faceLimit))
                  : math.max(fluxVal, math.min(0f, -faceLimit));
            }
          }
        }

        float finalFlux = fluxVal * mask;
        FaceGasFlux[fluxOffset + fIdx] = finalFlux;
      }
    }

    private int GetFaceCount(int regIdx)
    {
      if (regIdx == SentinelRegionIndex) return 1;
      if (regIdx >= DynamicRegionPoolStart)
      {
        return math.max(1, DynRegionFaceCount[regIdx - DynamicRegionPoolStart]);
      }
      return math.max(1, RegionFaceCounts[regIdx]);
    }
  }
}