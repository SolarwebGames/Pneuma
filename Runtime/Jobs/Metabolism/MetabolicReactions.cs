using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

using SolarWeb.Pneuma.Logging;
using SolarWeb.Pneuma.Metabolism;

namespace SolarWeb.Pneuma.Jobs.Metabolism
{
  [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
  public struct MetabolicReactions : IJobParallelFor
  {
    public float TimeStep;
    public MetabolismProperties Props;
    public int GasCount;
    public int GlobinCount;
    public int BlockSize;
    public int GlobinReactionCount;
    public int Count;

    public NativeArray<long> PlasmaStorage;
    public NativeArray<GlobinState> GlobinLevels;
    [ReadOnly] public NativeArray<MetabolicReactionProperties> Reactions;

    public NativeArray<long> TotalFreePlasma;

    public void Execute(int blockIdx)
    {
      int blockBase = blockIdx * GasCount * BlockSize;
      int globinBlockBase = blockIdx * GlobinCount * BlockSize;
      int entityBase = blockIdx * BlockSize;

      // =======================================================================
      // GLOBIN-SOURCED REACTIONS
      // =======================================================================
      for (int r = 0; r < GlobinReactionCount; r++)
      {
        var rx = Reactions[r];
        int globinBase = globinBlockBase + (rx.TargetGlobinId * BlockSize);
        long demand = (long)(rx.TargetMetabolicRatePerSecond * TimeStep);

#if DEBUG && UNITY_BURST_EXPERIMENTAL_LOOP_INTRINSICS
        Unity.Burst.CompilerServices.Loop.ExpectVectorized();
#endif
        for (int lane = 0; lane < BlockSize; lane++)
        {
          if (entityBase + lane >= Count) continue;

          var globin = GlobinLevels[globinBase + lane];
          long tfp = TotalFreePlasma[entityBase + lane];
          long space = math.max(0L, (long)Props.PlasmaCapacityUMol - tfp);

          long maxConsumeBySpace = rx.OutputId >= 0 ? (long)(space * 100000.0 / math.max(1, rx.EfficiencyBP)) : long.MaxValue;
          long effectiveDemand = math.min(demand, maxConsumeBySpace);

#if DEBUG && UNITY_BURST_EXPERIMENTAL_LOOP_INTRINSICS
          Unity.Burst.CompilerServices.Loop.ExpectVectorized();
#endif
          bool valid = (globin.OccupantGasId == rx.InputId) & (globin.OccupiedUMol > 0);
          long consumed = valid ? math.min(globin.OccupiedUMol, effectiveDemand) : 0L;

          globin.OccupiedUMol -= consumed;
          globin.OccupantGasId = globin.OccupiedUMol == 0 ? -1 : globin.OccupantGasId;
          GlobinLevels[globinBase + lane] = globin;

          if (rx.OutputId >= 0 && consumed > 0)
          {
            long produced = (consumed * rx.EfficiencyBP) / 100000;
            PlasmaStorage[blockBase + (rx.OutputId * BlockSize) + lane] += produced;
            TotalFreePlasma[entityBase + lane] = tfp + produced;
          }
        }
      }

      // =======================================================================
      // FREE PLASMA REACTIONS
      // =======================================================================
      for (int r = GlobinReactionCount; r < Reactions.Length; r++)
      {
        var rx = Reactions[r];
        int inputBase = blockBase + (rx.InputId * BlockSize);
        long demand = (long)(rx.TargetMetabolicRatePerSecond * TimeStep);

#if DEBUG && UNITY_BURST_EXPERIMENTAL_LOOP_INTRINSICS
        Unity.Burst.CompilerServices.Loop.ExpectVectorized();
#endif
        for (int lane = 0; lane < BlockSize; lane++)
        {
          if (entityBase + lane >= Count) continue;

          long tfp = TotalFreePlasma[entityBase + lane];
          long space = math.max(0L, (long)Props.PlasmaCapacityUMol - tfp);

          long maxConsumeBySpace = rx.OutputId >= 0 ? (long)(space * 100000.0 / math.max(1, rx.EfficiencyBP)) : long.MaxValue;
          long effectiveDemand = math.min(demand, maxConsumeBySpace);

          long consumed = math.min(PlasmaStorage[inputBase + lane], effectiveDemand);
          PlasmaStorage[inputBase + lane] -= consumed;

          if (consumed > 0)
          {
            long produced = 0;
            if (rx.OutputId >= 0)
            {
              produced = (consumed * rx.EfficiencyBP) / 100000;
              PlasmaStorage[blockBase + (rx.OutputId * BlockSize) + lane] += produced;
            }
            TotalFreePlasma[entityBase + lane] = tfp - consumed + produced;
          }
        }
      }
    }
  }
}
