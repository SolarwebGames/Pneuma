using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

using SolarWeb.Pneuma.Logging;
using SolarWeb.Pneuma.Metabolism;

namespace SolarWeb.Pneuma.Jobs.Metabolism
{
  [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
  public struct Breathe : IJobParallelFor
  {
    public float TimeStep;
    public MetabolismProperties Props;
    public int GasCount;
    public int BlockSize;
    public int Count;
    public int RegionStride;
    public int SentinelRegionIndex;

    [ReadOnly] public NativeArray<int> WorldIndices;
    [ReadOnly] public NativeArray<int> WorldToRegionIndex;
    [ReadOnly] public NativeArray<float> LungEfficiencies;

    [ReadOnly] public NativeArray<long> RegionUMoles;
    [ReadOnly] public NativeArray<long> RegionTotalUMoles;
    [ReadOnly] public NativeArray<float> RegionPressureKpa;

    [NativeDisableParallelForRestriction] public NativeArray<long> InternalStorage;
    [NativeDisableParallelForRestriction] public NativeArray<long> EntityLungNetFlux;

    [ReadOnly] public NativeArray<bool> HasExternalSupply;
    [ReadOnly] public NativeArray<long> ExternalLungSupply;
    [NativeDisableParallelForRestriction] public NativeArray<long> ExternalNetConsumed;

    public unsafe void Execute(int blockIdx)
    {
      int entityBase = blockIdx * BlockSize;
      int blockBase = blockIdx * GasCount * BlockSize;

      // Per-lane scratch — true stack allocation, zero atomic/safety overhead
      int* regIdx = stackalloc int[BlockSize];
      bool* isSentinel = stackalloc bool[BlockSize];
      bool* useExternal = stackalloc bool[BlockSize];
      long* totalAtmoMoles = stackalloc long[BlockSize];
      float* pressureMult = stackalloc float[BlockSize];
      long* ventilateAmt = stackalloc long[BlockSize];
      long* totalLungMoles = stackalloc long[BlockSize];
      long* targetMoles = stackalloc long[BlockSize];
      float* invTotalAtmo = stackalloc float[BlockSize];
      float* invTotalLung = stackalloc float[BlockSize];

      // totalLungMoles is an accumulator — must be zeroed before Phase 2.
      // All other arrays are fully written in Phase 1 before being read.
      for (int l = 0; l < BlockSize; l++) totalLungMoles[l] = 0;

      // ── Phase 1: Per-lane scalar setup (scatter reads, not vectorizable) ────────
      for (int lane = 0; lane < BlockSize; lane++)
      {
        int e = entityBase + lane;
        if (e >= Count)
        {
          regIdx[lane] = SentinelRegionIndex;
          isSentinel[lane] = true;
          continue;
        }

        int worldIdx = WorldIndices[e];
        int rIdx = (worldIdx >= 0 && worldIdx < WorldToRegionIndex.Length)
            ? WorldToRegionIndex[worldIdx] : -1;
        if (rIdx < 0) rIdx = SentinelRegionIndex;

        regIdx[lane] = rIdx;
        isSentinel[lane] = (rIdx == SentinelRegionIndex);
        useExternal[lane] = HasExternalSupply[e];

        if (useExternal[lane])
        {
          long extTotal = 0;
          for (int g = 0; g < GasCount; g++)
            extTotal += ExternalLungSupply[blockBase + g * BlockSize + lane];
          totalAtmoMoles[lane] = extTotal;
          pressureMult[lane] = 1.0f;
        }
        else
        {
          totalAtmoMoles[lane] = RegionTotalUMoles[rIdx];
          pressureMult[lane] = math.clamp(RegionPressureKpa[rIdx] / 101.325f, 0f, 10f);
        }

        invTotalAtmo[lane] = totalAtmoMoles[lane] > 1 ? 1.0f / totalAtmoMoles[lane] : 0f;
        ventilateAmt[lane] = (long)(Props.VentilationRate * TimeStep * LungEfficiencies[e]);
      }

      // ── Phase 2: Accumulate totalLungMoles per lane (contiguous — Burst SIMD) ──
      for (int g = 0; g < GasCount; g++)
      {
        int gasBase = blockBase + g * BlockSize;
#if DEBUG && UNITY_BURST_EXPERIMENTAL_LOOP_INTRINSICS
        Unity.Burst.CompilerServices.Loop.ExpectVectorized();
#endif
        for (int lane = 0; lane < BlockSize; lane++)
          totalLungMoles[lane] += InternalStorage[gasBase + lane];
      }

      // ── Phase 3: Per-lane scalar calcs ───────────────────────────────────────────
      for (int lane = 0; lane < BlockSize; lane++)
      {
        if (entityBase + lane >= Count) continue;
        invTotalLung[lane] = totalLungMoles[lane] > 0 ? 1.0f / totalLungMoles[lane] : 0f;
        targetMoles[lane] = (long)(Props.LungCapacityUMol * pressureMult[lane]);
      }

      // ── Phase 4: Per-gas ventilation + direct lung update (inner loop SIMD) ─────
      for (int g = 0; g < GasCount; g++)
      {
        int gasBase = blockBase + g * BlockSize;

#if DEBUG && UNITY_BURST_EXPERIMENTAL_LOOP_INTRINSICS
        Unity.Burst.CompilerServices.Loop.ExpectVectorized();
#endif
        for (int lane = 0; lane < BlockSize; lane++)
        {
          int e = entityBase + lane;
          if (e >= Count)
          {
            EntityLungNetFlux[gasBase + lane] = 0;
            continue;
          }

          long atmoMolesForGas = useExternal[lane]
              ? ExternalLungSupply[gasBase + lane]
              : RegionUMoles[g * RegionStride + regIdx[lane]];

          float atmoFrac = atmoMolesForGas * invTotalAtmo[lane];

          if (useExternal[lane] && atmoMolesForGas == 0)
          {
            // Waste gas with no external supply — vent scrubbed gas to room
            long scrubbed = InternalStorage[gasBase + lane];
            InternalStorage[gasBase + lane] = 0;
            EntityLungNetFlux[gasBase + lane] = scrubbed; // positive = exhale to region
            ExternalNetConsumed[gasBase + lane] = 0;
            continue;
          }

          long lungAmt = InternalStorage[gasBase + lane];
          long exhaustAmt = math.min(
              (long)(ventilateAmt[lane] * (lungAmt * invTotalLung[lane])),
              lungAmt);
          long fillGap = math.max(0L, targetMoles[lane] - totalLungMoles[lane]);
          long gapFillAmt = (long)(fillGap * atmoFrac);
          long inhaleAmt = (long)(ventilateAmt[lane] * atmoFrac) + gapFillAmt;

          long netToAtmosphere = exhaustAmt - inhaleAmt;

          if (useExternal[lane])
          {
            InternalStorage[gasBase + lane] = math.max(0L, lungAmt - netToAtmosphere);
            ExternalNetConsumed[gasBase + lane] = inhaleAmt - exhaustAmt;
            EntityLungNetFlux[gasBase + lane] = 0; // no region exchange
          }
          else if (isSentinel[lane])
          {
            InternalStorage[gasBase + lane] = math.max(0L, lungAmt - netToAtmosphere);
            EntityLungNetFlux[gasBase + lane] = 0; // discard (infinite outdoors)
          }
          else
          {
            // Direct update — no exchanger lag
            InternalStorage[gasBase + lane] -= netToAtmosphere;
            EntityLungNetFlux[gasBase + lane] = netToAtmosphere;
          }
        }
      }
    }
  }
}
