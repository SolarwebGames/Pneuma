using Unity.Collections;
using Unity.Jobs;
using SolarWeb.Pneuma.Grid;
using SolarWeb.Pneuma.Jobs.Plants;

namespace SolarWeb.Pneuma.Simulation
{
  public class PlantAtmosphereSimulator
  {
    private readonly AtmosphereManager manager;

    public PlantAtmosphereSimulator(AtmosphereManager manager)
    {
      this.manager = manager;
    }

    public JobHandle DoUpdate(float timeStep, JobHandle dependency)
    {
      var state = manager.State;
      var registry = state.PlantMetabolismRegistry;
      var population = state.RegionPlantPopulation;
      var plantStates = state.RegionPlantStates;
      var gasData = manager.GasRegistry.GasData;

      int groupCount = population.ProfileIdx.Length;
      if (groupCount == 0) return dependency;

      var groupFluxScales = new NativeArray<float>(groupCount, Allocator.TempJob);

      var evalJob = new EvaluatePlantAtmosphere
      {
        TimeStep = timeStep,
        GasCount = state.GasCount,
        SentinelRegionIndex = state.SentinelRegionIndex,
        RegionStride = state.RegionStride,
        MaxReagentsPerProfile = PlantMetabolismRegistry.MaxReagentsPerProfile,

        PerProfileCorrosiveness = registry.EffectiveCorrosiveness,
        PerProfileBioInterference = registry.EffectiveBioInterference,
        IonizingPotential = gasData.IonizingPotential,

        InputGasIds = registry.InputGasIds,
        InputMinPressuresPa = registry.InputMinPressuresPa,
        InputIsRoot = registry.InputIsRoot,
        InputCounts = registry.InputCounts,

        RadiationAffinity = registry.RadiationAffinity,
        ChemicalTolerance = registry.ChemicalTolerance,

        GroupProfileIdx = population.ProfileIdx.AsArray(),
        GroupLeafRegionIdx = population.LeafRegionIdx.AsArray(),
        GroupRootInRegionIdx = population.RootInRegionIdx.AsArray(),
        GroupRootOutRegionIdx = population.RootOutRegionIdx.AsArray(),
        GroupCountMature = population.CountMature.AsArray(),
        GroupCountGrowing = population.CountGrowing.AsArray(),
        GroupCountSowing = population.CountSowing.AsArray(),

        GroupEfficiency = plantStates.Efficiency.AsArray(),
        GroupChemicalStress = plantStates.ChemicalStress.AsArray(),
        GroupRadiologicalStress = plantStates.RadiologicalStress.AsArray(),
        GroupFluxScale = groupFluxScales,

        RegionUMoles = state.RegionGasComposition.uMoles,
        RegionPressureKpa = state.RegionGasComposition.PressureKpa
      };

      var evalHandle = evalJob.Schedule(groupCount, 64, dependency);

      var accJob = new AccumulatePlantFlux
      {
        GroupCount = groupCount,
        SentinelRegionIndex = state.SentinelRegionIndex,
        RegionStride = state.RegionStride,
        MaxReagentsPerProfile = PlantMetabolismRegistry.MaxReagentsPerProfile,

        InputGasIds = registry.InputGasIds,
        InputMolarRatios = registry.InputMolarRatios,
        InputIsRoot = registry.InputIsRoot,
        InputCounts = registry.InputCounts,

        OutputGasIds = registry.OutputGasIds,
        OutputMolarRatios = registry.OutputMolarRatios,
        OutputIsRoot = registry.OutputIsRoot,
        OutputCounts = registry.OutputCounts,

        GroupProfileIdx = population.ProfileIdx.AsArray(),
        GroupLeafRegionIdx = population.LeafRegionIdx.AsArray(),
        GroupRootInRegionIdx = population.RootInRegionIdx.AsArray(),
        GroupRootOutRegionIdx = population.RootOutRegionIdx.AsArray(),

        GroupFluxScale = groupFluxScales,
        FluxAccumulator = state.RegionGasComposition.RegionNetFlux
      };

      return accJob.Schedule(state.GasCount, 1, evalHandle);
    }
  }
}
