using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

using SolarWeb.Pneuma.Logging;
using SolarWeb.Pneuma.Metabolism;

namespace SolarWeb.Pneuma.Jobs.Metabolism
{
  [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
  public struct PlasmaDiffusion : IJobParallelFor
  {
    public float TimeStep;
    public MetabolismProperties Props;
    public float InvPlasmaCapacity;
    public int BlockSize;
    public int Count;

    public int GasCount;
    public int GlobinCount;

    public NativeArray<long> InternalStorage;
    public NativeArray<long> PlasmaStorage;
    public NativeArray<GlobinState> GlobinLevels;

    public NativeArray<long> TotalFreePlasma;
    [ReadOnly] public NativeArray<float> BohrShifts;

    [ReadOnly] public NativeArray<int> GlobinGasIds;
    [ReadOnly] public NativeArray<int> GlobinGasGlobinIds;
    [ReadOnly] public NativeArray<float> GlobinGasBindAffinities;
    [ReadOnly] public NativeArray<long> GlobinCapacities;
    [ReadOnly] public NativeArray<float> InvGlobinCapacities;
    [ReadOnly] public NativeArray<float> GlobinHillCoeffs;
    [ReadOnly] public NativeArray<float> GlobinP50Fracs;
    [ReadOnly] public NativeArray<float> GlobinGasLungUptakeFactors;
    [ReadOnly] public NativeArray<float> GlobinGasLungReleaseFactors;

    [ReadOnly] public NativeArray<int> FreeGasIds;
    [ReadOnly] public NativeArray<float> FreeGasUptakeFactors;
    [ReadOnly] public NativeArray<float> FreeGasReleaseFactors;

    [ReadOnly] public NativeArray<float> PlasmaExchangeEfficiencies;
    [ReadOnly] public NativeArray<float> EntitySolubilityScale;

    public void Execute(int blockIdx)
    {
      int blockBase = blockIdx * GasCount * BlockSize;
      int globinBlockBase = blockIdx * GlobinCount * BlockSize;
      int entityBase = blockIdx * BlockSize;

      // =======================================================================
      // GLOBIN-MEDIATED PATH (Sub-step A: Lung <-> Plasma)
      // =======================================================================
      for (int bi = 0; bi < GlobinGasIds.Length; bi++)
      {
        int g = GlobinGasIds[bi];
        int gasBase = blockBase + (g * BlockSize);
        float partition = GlobinGasLungUptakeFactors[bi];
        float relFactor = GlobinGasLungReleaseFactors[bi];
        float partFrac = partition / (1.0f + partition);

#if DEBUG && UNITY_BURST_EXPERIMENTAL_LOOP_INTRINSICS
        Unity.Burst.CompilerServices.Loop.ExpectVectorized();
#endif
        for (int lane = 0; lane < BlockSize; lane++)
        {
          float sol = EntitySolubilityScale[entityBase + lane];
          float eff = PlasmaExchangeEfficiencies[entityBase + lane];
          float baseDiff = (float)Props.PlasmaExchangeRate * InvPlasmaCapacity * eff;

          long lungAmt = InternalStorage[gasBase + lane];
          long plasmaAmt = PlasmaStorage[gasBase + lane];

          float bEq = (float)(lungAmt + plasmaAmt) * partFrac;
          float step = math.min(0.5f, baseDiff * relFactor * sol * TimeStep);
          long xfer = (long)((bEq - (float)plasmaAmt) * step);

          long tfp = TotalFreePlasma[entityBase + lane];
          long spaceInPlasma = math.max(0L, (long)Props.PlasmaCapacityUMol - tfp);

          xfer = math.max(-plasmaAmt, math.min(xfer, math.min(lungAmt, spaceInPlasma)));

          InternalStorage[gasBase + lane] = lungAmt - xfer;
          PlasmaStorage[gasBase + lane] = plasmaAmt + xfer;
          TotalFreePlasma[entityBase + lane] = tfp + xfer;

        }
      }

      // =======================================================================
      // GLOBIN-MEDIATED PATH (Sub-step B: Plasma <-> Carrier)
      // =======================================================================
      for (int bi = 0; bi < GlobinGasIds.Length; bi++)
      {
        int g = GlobinGasIds[bi];
        int globinIdx = GlobinGasGlobinIds[bi];
        int gasBase = blockBase + (g * BlockSize);
        int globinBase = globinBlockBase + (globinIdx * BlockSize);

        float p50_base = GlobinP50Fracs[bi];
        float n = GlobinHillCoeffs[bi];
        float bindAffinity = GlobinGasBindAffinities[bi];
        long globinCap = GlobinCapacities[globinIdx];
        float invGlobinCap = InvGlobinCapacities[globinIdx];

#if DEBUG && UNITY_BURST_EXPERIMENTAL_LOOP_INTRINSICS
        Unity.Burst.CompilerServices.Loop.ExpectVectorized();
#endif
        for (int lane = 0; lane < BlockSize; lane++)
        {
          if (entityBase + lane >= Count) continue;

          float eff = PlasmaExchangeEfficiencies[entityBase + lane];
          float scaledExRate = (float)Props.PlasmaExchangeRate * TimeStep * eff;
          float bohr = BohrShifts[entityBase + lane];

          long lungAmt = InternalStorage[gasBase + lane];
          float lungFrac = (float)lungAmt / math.max(1f, (float)Props.LungCapacityUMol);

          float p50_t = math.lerp(p50_base, p50_base * bohr, 0.2f);
          float ratio = p50_t > 0f ? lungFrac / p50_t : 0f;
          float ratioN = math.pow(math.max(0f, ratio), n);
          float eqSat = ratioN / (1f + ratioN);

          var globin = GlobinLevels[globinBase + lane];
          float saturation = (float)globin.OccupiedUMol * invGlobinCap;
          float delta = eqSat - saturation;

          if (math.abs(delta) < 1e-5f) continue;

          long rate = (long)(scaledExRate * bindAffinity * math.abs(delta));
          long plasmaAmt = PlasmaStorage[gasBase + lane];
          long maxXfer = (long)Props.LungCapacityUMol / 20;
          long tfp = TotalFreePlasma[entityBase + lane];

          long transfer;
          if (delta > 0)
          {
            long available = globinCap - globin.OccupiedUMol;
            bool canBind = (globin.OccupantGasId == -1) | (globin.OccupantGasId == g);
            transfer = canBind ? math.min(rate, math.min(plasmaAmt, math.min(available, maxXfer))) : 0L;
            PlasmaStorage[gasBase + lane] = plasmaAmt - transfer;
            globin.OccupiedUMol += transfer;
            globin.OccupantGasId = transfer > 0 ? g : globin.OccupantGasId;
            TotalFreePlasma[entityBase + lane] = tfp - transfer;
          }
          else
          {
            bool isThisGas = (globin.OccupantGasId == g);
            long spaceInPlasma = math.max(0L, (long)Props.PlasmaCapacityUMol - tfp);
            transfer = isThisGas ? math.min(rate, math.min(globin.OccupiedUMol, spaceInPlasma)) : 0L;
            PlasmaStorage[gasBase + lane] = plasmaAmt + transfer;
            globin.OccupiedUMol -= transfer;
            globin.OccupantGasId = globin.OccupiedUMol == 0 ? -1 : globin.OccupantGasId;
            TotalFreePlasma[entityBase + lane] = tfp + transfer;
          }
          GlobinLevels[globinBase + lane] = globin;

        }
      }

      // =======================================================================
      // FREE DISSOLVED PATH
      // =======================================================================
      for (int fi = 0; fi < FreeGasIds.Length; fi++)
      {
        int g = FreeGasIds[fi];
        int gasBase = blockBase + (g * BlockSize);
        float partition = FreeGasUptakeFactors[fi];
        float relFactor = FreeGasReleaseFactors[fi];
        float partFrac = partition / (1.0f + partition);

#if DEBUG && UNITY_BURST_EXPERIMENTAL_LOOP_INTRINSICS
        Unity.Burst.CompilerServices.Loop.ExpectVectorized();
#endif
        for (int lane = 0; lane < BlockSize; lane++)
        {
          float sol = EntitySolubilityScale[entityBase + lane];
          float eff = PlasmaExchangeEfficiencies[entityBase + lane];
          float baseDiff = (float)Props.PlasmaExchangeRate * InvPlasmaCapacity * eff;

          long tfp = TotalFreePlasma[entityBase + lane];
          float fSat = math.saturate((float)((long)Props.PlasmaCapacityUMol - tfp) * InvPlasmaCapacity);

          long lungAmt = InternalStorage[gasBase + lane];
          long plasmaAmt = PlasmaStorage[gasBase + lane];

          float bEq = (float)(lungAmt + plasmaAmt) * partFrac;
          float grad = bEq - (float)plasmaAmt;

          float rMult = math.select(1.0f, fSat * sol, grad > 0f);
          float step = math.min(0.5f, baseDiff * relFactor * rMult * TimeStep);
          long xfer = (long)(grad * step);
          long spaceInPlasma = math.max(0L, (long)Props.PlasmaCapacityUMol - tfp);

          xfer = math.max(-plasmaAmt, math.min(xfer, math.min(lungAmt, spaceInPlasma)));

          InternalStorage[gasBase + lane] = lungAmt - xfer;
          PlasmaStorage[gasBase + lane] = plasmaAmt + xfer;
          TotalFreePlasma[entityBase + lane] = tfp + xfer;
        }
      }
    }
  }
}