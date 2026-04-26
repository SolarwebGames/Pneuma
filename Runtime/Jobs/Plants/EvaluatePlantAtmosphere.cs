using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace SolarWeb.Pneuma.Jobs.Plants
{
  [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
  public struct EvaluatePlantAtmosphere : IJobParallelFor
  {
    public float TimeStep;
    public int GasCount;
    public int SentinelRegionIndex;
    public int RegionStride;
    public int MaxReagentsPerProfile;

    // --- Gas Hazards (per-profile effective values) ---
    [ReadOnly] public NativeArray<float> PerProfileCorrosiveness;   // [pIdx * GasCount + gasId]
    [ReadOnly] public NativeArray<float> PerProfileBioInterference; // [pIdx * GasCount + gasId]
    [ReadOnly] public NativeArray<float> IonizingPotential;

    // --- Plant Metabolic Reactions (SoA) ---
    [ReadOnly] public NativeArray<int> InputGasIds;        // [pIdx * MaxReagentsPerProfile + slot]
    [ReadOnly] public NativeArray<float> InputMinPressuresPa;
    [ReadOnly] public NativeArray<bool> InputIsRoot;
    [ReadOnly] public NativeArray<int> InputCounts;        // [pIdx]

    [ReadOnly] public NativeArray<float> RadiationAffinity;
    [ReadOnly] public NativeArray<float> ChemicalTolerance;

    // --- Plant Groups (Input SoA) ---
    [ReadOnly] public NativeArray<int> GroupProfileIdx;
    [ReadOnly] public NativeArray<int> GroupLeafRegionIdx;
    [ReadOnly] public NativeArray<int> GroupRootInRegionIdx;
    [ReadOnly] public NativeArray<int> GroupRootOutRegionIdx;
    [ReadOnly] public NativeArray<int> GroupCountMature;
    [ReadOnly] public NativeArray<int> GroupCountGrowing;
    [ReadOnly] public NativeArray<int> GroupCountSowing;

    // --- Grid State ---
    [ReadOnly] public NativeArray<long> RegionUMoles;      // [gasId * RegionStride + regIdx]
    [ReadOnly] public NativeArray<float> RegionPressureKpa;

    // --- Outputs (SoA) ---
    [WriteOnly] public NativeArray<float> GroupEfficiency;
    [WriteOnly] public NativeArray<float> GroupChemicalStress;
    [WriteOnly] public NativeArray<float> GroupRadiologicalStress;
    [WriteOnly] public NativeArray<float> GroupFluxScale;

    public void Execute(int index)
    {
      int pIdx = GroupProfileIdx[index];
      int leafReg = GroupLeafRegionIdx[index];
      int rootInReg = GroupRootInRegionIdx[index];
      int rootOutReg = GroupRootOutRegionIdx[index];

      if (leafReg < 0) leafReg = SentinelRegionIndex;

      float leafPressurePa = RegionPressureKpa[leafReg] * 1000f;
      long leafTotalMoles = GetTotalRegionUMoles(leafReg);

      int countMature = GroupCountMature[index];
      int countGrowing = GroupCountGrowing[index];
      int countSowing = GroupCountSowing[index];

      if (countMature + countGrowing + countSowing == 0)
      {
        GroupEfficiency[index] = 0f;
        GroupChemicalStress[index] = 0f;
        GroupRadiologicalStress[index] = 0f;
        GroupFluxScale[index] = 0f;
        return;
      }

      // 1. Environmental Hazard Evaluation (Leaves and Roots)
      float chemStress = 0f;
      float bioInterference = 0f;
      float radStress = 0f;

      // Leaf-borne hazards
      for (int g = 0; g < GasCount; g++)
      {
        long gasUMoles = RegionUMoles[(g * RegionStride) + leafReg];
        float fraction = (float)gasUMoles / math.max(1, leafTotalMoles);
        int profGasIdx = (pIdx * GasCount) + g;

        chemStress += fraction * PerProfileCorrosiveness[profGasIdx];
        bioInterference += fraction * PerProfileBioInterference[profGasIdx];
        radStress += fraction * IonizingPotential[g];
      }

      // Root-borne hazards (Input and Output pipes)
      if (rootInReg >= 0)
      {
        long rootInTotalMoles = GetTotalRegionUMoles(rootInReg);
        for (int g = 0; g < GasCount; g++)
        {
          long gasUMoles = RegionUMoles[(g * RegionStride) + rootInReg];
          float fraction = (float)gasUMoles / math.max(1, rootInTotalMoles);
          int profGasIdx = (pIdx * GasCount) + g;

          chemStress += fraction * PerProfileCorrosiveness[profGasIdx];
          bioInterference += fraction * PerProfileBioInterference[profGasIdx];
          radStress += fraction * IonizingPotential[g];
        }
      }
      if (rootOutReg >= 0 && rootOutReg != rootInReg)
      {
        long rootOutTotalMoles = GetTotalRegionUMoles(rootOutReg);
        for (int g = 0; g < GasCount; g++)
        {
          long gasUMoles = RegionUMoles[(g * RegionStride) + rootOutReg];
          float fraction = (float)gasUMoles / math.max(1, rootOutTotalMoles);
          int profGasIdx = (pIdx * GasCount) + g;

          chemStress += fraction * PerProfileCorrosiveness[profGasIdx];
          bioInterference += fraction * PerProfileBioInterference[profGasIdx];
          radStress += fraction * IonizingPotential[g];
        }
      }

      float tolerance = ChemicalTolerance[pIdx];
      float finalChemStress = (chemStress + bioInterference) * (1f - tolerance);
      float radAffinity = RadiationAffinity[pIdx];
      float finalRadStress = radAffinity < 0f ? radStress * math.abs(radAffinity) : 0f;

      // 2. Metabolic Efficiency — Liebig's law over all input gases
      float efficiency = 1f;
      int inCount = InputCounts[pIdx];
      for (int s = 0; s < inCount; s++)
      {
        int slot = pIdx * MaxReagentsPerProfile + s;
        int gasId = InputGasIds[slot];
        if (gasId < 0) continue;

        bool isRoot = InputIsRoot[slot];

        // Only enforce root constraints if the plant is in a hydroponics/pipe-connected edifice.
        // Plants in soil (rootInReg < 0) are assumed to have their root nutrient needs satisfied.
        if (isRoot && rootInReg < 0) continue;

        int targetReg = isRoot ? rootInReg : leafReg;

        long targetMoles = GetTotalRegionUMoles(targetReg);
        float targetPressure = RegionPressureKpa[targetReg] * 1000f;

        float partialPa = targetMoles > 0
          ? ((float)RegionUMoles[gasId * RegionStride + targetReg] / targetMoles) * targetPressure
          : 0f;

        float minPa = math.max(0.01f, InputMinPressuresPa[slot]);
        efficiency = math.min(efficiency, math.saturate(partialPa / minPa));
      }

      // Radiotrophic boost
      if (radAffinity > 0f) efficiency *= (1f + radStress * radAffinity);

      GroupEfficiency[index] = efficiency;
      GroupChemicalStress[index] = finalChemStress;
      GroupRadiologicalStress[index] = finalRadStress;

      // 3. Prepare Flux Scale for Phase 2
      float totalBioMassActivity = (countMature * 1.0f) + (countGrowing * 0.5f) + (countSowing * 0.1f);
      GroupFluxScale[index] = totalBioMassActivity * efficiency * 10.0f * TimeStep;
    }

    private long GetTotalRegionUMoles(int regIdx)
    {
      long total = 0;
      for (int g = 0; g < GasCount; g++) total += RegionUMoles[g * RegionStride + regIdx];
      return total;
    }
  }
}
