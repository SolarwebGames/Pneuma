using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

using SolarWeb.Pneuma.Data;

namespace SolarWeb.Pneuma.Jobs.Diffusion
{
  [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
  public struct DetectFaceOverpressure : IJobParallelFor
  {
    [ReadOnly] public NativeArray<int> FaceRegionA;
    [ReadOnly] public NativeArray<int> FaceRegionB;
    [ReadOnly] public NativeArray<float> PressureKpa;
    [ReadOnly] public NativeArray<float> FaceMaxPressureDeltaKpa;
    public int SentinelRegionIndex;

    [WriteOnly] public NativeList<OverpressureEvent>.ParallelWriter OverpressureEvents;

    public void Execute(int fIdx)
    {
      int rA = FaceRegionA[fIdx];
      int rB = FaceRegionB[fIdx];

      float pA = PressureKpa[rA];
      float pB = PressureKpa[rB];

      float deltaP = math.abs(pA - pB);
      float maxDeltaP = FaceMaxPressureDeltaKpa[fIdx];

      if (deltaP > maxDeltaP)
      {
        OverpressureEvents.AddNoResize(new OverpressureEvent
        {
          IsFaceEvent = true,
          Index = fIdx,
          PressureKpa = deltaP
        });
      }
    }
  }
}
