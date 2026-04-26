using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace SolarWeb.Pneuma.Diffusion.Jobs
{
  [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
  public struct PrecomputeFacePhysics : IJobParallelFor
  {
    [ReadOnly] public NativeArray<float> PressureKpa;
    [ReadOnly] public NativeArray<float> TemperatureK;
    [ReadOnly] public NativeArray<float> RegionSpeedOfSound;
    [ReadOnly] public NativeArray<float> RegionInvVolConst;
    [ReadOnly] public NativeArray<float> RegionTFactor;
    [ReadOnly] public NativeArray<long> TotalUMoles;

    [ReadOnly] public NativeArray<int> FaceRegionA;
    [ReadOnly] public NativeArray<int> FaceRegionB;

    [ReadOnly] public NativeArray<float> FaceGasPermeability;
    [ReadOnly] public NativeArray<float> FaceSurfaceArea;

    [ReadOnly] public NativeArray<float> FaceRegulatorKpa;

    [ReadOnly] public NativeArray<int> RegionFaceCounts;
    [ReadOnly] public NativeArray<int> DynRegionFaceCount;
    public int DynamicRegionPoolStart;

    [ReadOnly] public NativeArray<float> FacePressureOffset;

    public int SentinelRegionIndex;
    public float TimeStep;
    [ReadOnly] public NativeArray<float> FaceActivePumpRate;

    [WriteOnly] public NativeArray<float> FaceLimitedVel;
    [WriteOnly] public NativeArray<float> FaceMaxSafeAdvection;
    [WriteOnly] public NativeArray<float> FaceCombinedTFactor;

    public void Execute(int fIdx)
    {
      int iA = FaceRegionA[fIdx];
      int iB = FaceRegionB[fIdx];

      if (iA == SentinelRegionIndex && iB == SentinelRegionIndex)
      {
        FaceLimitedVel[fIdx] = 0f;
        FaceMaxSafeAdvection[fIdx] = 0f;
        FaceCombinedTFactor[fIdx] = 0f;
        return;
      }

      float pA = PressureKpa[iA];
      float pB = PressureKpa[iB];

      float maxOutputKpa = FaceRegulatorKpa[fIdx];
      if (maxOutputKpa >= 0f)
      {
        if (pA > maxOutputKpa) pA = maxOutputKpa;
      }

      float totalMolesA = TotalUMoles[iA];
      float totalMolesB = TotalUMoles[iB];

      float deltaP = (pA - pB) + FacePressureOffset[fIdx];
      float absDeltaP = math.abs(deltaP);

      float avgSpeedOfSound = (RegionSpeedOfSound[iA] + RegionSpeedOfSound[iB]) * 0.5f;

      // rawVel here represents "Bulk Flow Capacity" (Area * VelocityFactor), not Velocity (m/s)
      // because FaceGasPermeability scales with the number of cells (Area).
      // Multiply absDeltaP by 1000 to convert kPa → Pa so the sqrt gives a correct m/s-scale velocity.
      float rawVel = math.sign(deltaP) * math.sqrt(absDeltaP * 1000f) * FaceGasPermeability[fIdx];

      // To clamp correctly, we must convert Flow -> Velocity -> Clamp -> Flow.
      // Use FaceGasPermeability (Effective Area) to get the velocity through the permeable part.
      // Using FaceSurfaceArea (Geometric Area) would underestimate velocity for small leaks, bypassing the speed limit.
      float permeability = FaceGasPermeability[fIdx];
      float velocity = 0f;

      if (permeability > 1e-6f)
      {
        velocity = rawVel / permeability; // m/s estimate
      }

      float clampedVelocity = math.clamp(velocity, -avgSpeedOfSound, avgSpeedOfSound);

      FaceLimitedVel[fIdx] = clampedVelocity * permeability;

      float denom = (RegionInvVolConst[iA] * TemperatureK[iA]) + (RegionInvVolConst[iB] * TemperatureK[iB]);

      // sourceTotalMoles is used for the safety limit to prevent draining more than exists.
      // Both terms must be in consistent mol units:
      //   term1: absDeltaP [kPa] * 1000 → Pa, divided by denom [Pa/mol] → mol
      //   term2: sourceTotalMoles [µmol] / 1e6 → mol, capped at 0.5/face to prevent oscillation
      // Divide the pressure term by destFaceCount so that N simultaneous inflows into a small
      // region share the equilibration budget (prevents 1-cell pressure spikes).
      // Use destFaceCount=1 for the sentinel (infinite sink) so outflow to space is not throttled.
      int sourceRegIdx = (deltaP > 0) ? iA : iB;
      int destRegIdx = (deltaP > 0) ? iB : iA;
      float sourceTotalMoles = (deltaP > 0) ? (float)totalMolesA : (float)totalMolesB;
      int sourceFaceCount = GetFaceCount(sourceRegIdx);
      int destFaceCount = GetFaceCount(destRegIdx);

      float molesToFix = absDeltaP * 1000f / (denom + 1e-6f);
      float pumpCapacity = FaceActivePumpRate[fIdx] * 1e-6f * TimeStep;

      FaceMaxSafeAdvection[fIdx] = math.min(
          (molesToFix * 0.25f / destFaceCount) + pumpCapacity,
          (sourceTotalMoles / 1e6f) * 0.5f / sourceFaceCount);

      FaceCombinedTFactor[fIdx] = (RegionTFactor[iA] + RegionTFactor[iB]) * 0.5f;
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