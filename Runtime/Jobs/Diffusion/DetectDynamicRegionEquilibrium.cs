using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace SolarWeb.Pneuma.Jobs.Diffusion
{
  /// <summary>
  /// Iterates the dynamic region pool and tracks how many consecutive frames each region has been
  /// within equilibrium tolerance of its parent. Once the counter reaches RequiredEquilibriumTicks,
  /// the region is enqueued for re-absorption.
  ///
  /// Equilibrium is satisfied when ALL hold simultaneously vs the parent region:
  ///   |P_dyn - P_parent| / max(P_parent, 0.001) < PressureTolerance
  ///   |T_dyn - T_parent|                          < TemperatureTolerance
  ///   max_g |x_dyn[g] - x_parent[g]|              < CompositionTolerance
  ///
  /// EquilibriumTicks[dynRegIdx] increments each frame all conditions hold, and resets to 0 the
  /// moment any condition fails. This guards against a single zero-flux tick during an oscillation
  /// triggering a premature merge, while still allowing regions with tiny residual equilibrium-level
  /// flux to accumulate ticks and eventually merge.
  /// </summary>
  [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
  public struct DetectDynamicRegionEquilibrium : IJob
  {
    [ReadOnly] public NativeArray<int> DynRegionParent;

    // Region physics
    [ReadOnly] public NativeArray<float> PressureKpa;
    [ReadOnly] public NativeArray<float> TemperatureK;
    [ReadOnly] public NativeArray<float> MolarFractions;

    public NativeArray<int> EquilibriumTicks;

    public int DynamicRegionPoolStart;
    public int MaxDynRegions;
    public int GasCount;
    public int RegionStride;
    public int SentinelRegionIndex;

    public int RequiredEquilibriumTicks;
    public float PressureTolerance;
    public float TemperatureTolerance;
    public float CompositionTolerance;

    public NativeQueue<int>.ParallelWriter MergeQueue;

    public void Execute()
    {
      for (int slot = 0; slot < MaxDynRegions; slot++)
      {
        int parentRegIdx = DynRegionParent[slot];
        if (parentRegIdx < 0)
          continue;

        int dynRegIdx = DynamicRegionPoolStart + slot;

        float pDyn = PressureKpa[dynRegIdx];
        float pParent = PressureKpa[parentRegIdx];
        float pDenom = math.max(pParent, 0.001f);

        if (math.abs(pDyn - pParent) / pDenom > PressureTolerance)
        {
          EquilibriumTicks[dynRegIdx] = 0;
          continue;
        }

        float tDyn = TemperatureK[dynRegIdx];
        float tParent = TemperatureK[parentRegIdx];

        if (math.abs(tDyn - tParent) > TemperatureTolerance)
        {
          EquilibriumTicks[dynRegIdx] = 0;
          continue;
        }

        bool compositionOk = true;
        for (int g = 0; g < GasCount; g++)
        {
          float xDyn = MolarFractions[(g * RegionStride) + dynRegIdx];
          float xParent = MolarFractions[(g * RegionStride) + parentRegIdx];
          if (math.abs(xDyn - xParent) > CompositionTolerance)
          {
            compositionOk = false;
            break;
          }
        }

        if (!compositionOk)
        {
          EquilibriumTicks[dynRegIdx] = 0;
          continue;
        }

        EquilibriumTicks[dynRegIdx]++;
        if (EquilibriumTicks[dynRegIdx] >= RequiredEquilibriumTicks)
          MergeQueue.Enqueue(dynRegIdx);
      }
    }
  }
}
