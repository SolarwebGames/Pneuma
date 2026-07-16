using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

using SolarWeb.Pneuma.Data;

namespace SolarWeb.Pneuma.Jobs.Diffusion
{
  [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
  public struct DetectRegionOverpressure : IJobParallelForDefer
  {
    [ReadOnly] public NativeArray<float> PressureKpa;
    [ReadOnly] public NativeArray<float> MaxPressureKpa;

    [WriteOnly] public NativeList<OverpressureEvent>.ParallelWriter OverpressureEvents;

    public void Execute(int rIdx)
    {
      float pressure = PressureKpa[rIdx];
      float maxP = MaxPressureKpa[rIdx];
      if (pressure > maxP)
      {
        OverpressureEvents.AddNoResize(new OverpressureEvent
        {
          IsFaceEvent = false,
          Index = rIdx,
          PressureKpa = pressure
        });
      }
    }
  }
}
