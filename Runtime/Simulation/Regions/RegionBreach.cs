using SolarWeb.Pneuma.Grid;
using SolarWeb.Pneuma.Jobs.Diffusion;
using Unity.Jobs;
using SolarWeb.Pneuma.Logging;

namespace SolarWeb.Pneuma.Simulation
{
  public class RegionBreach
  {
    private AtmosphereGrid State => manager.State;
    private readonly AtmosphereManager manager;

    public RegionBreach(AtmosphereManager manager)
    {
      this.manager = manager;
    }

    public JobHandle DoDetectBreach(JobHandle dependency = default)
    {
      if (State.RegionFaceBuffer.TotalFaceCount == 0) return dependency;

      var handle = new DetectRegionBreach
      {
        FaceRegionA = State.RegionFaceBuffer.FaceRegionA,
        FaceRegionB = State.RegionFaceBuffer.FaceRegionB,
        FaceGasFlux = State.RegionFaceBuffer.FaceGasFlux,
        RegionPressureKpa = State.RegionGasComposition.PressureKpa,
        RegionFaceToCellOffsets = State.RegionFaceBuffer.RegionFaceToCellOffsets,
        RegionFaceToCellCounts = State.RegionFaceBuffer.RegionFaceToCellCounts,
        RegionFaceToCellSimA = State.RegionFaceBuffer.RegionFaceToCellSimA,
        RegionFaceToCellSimB = State.RegionFaceBuffer.RegionFaceToCellSimB,
        SimToWorldIndex = State.GridLookups.SimToWorldIndex,
        PendingSplitWorldIndices = State.DynamicRegions.PendingSplitWorldIndices.AsParallelWriter(),
        GasCount = State.GasCount,
        FaceStride = State.RegionFaceBuffer.FaceStride,
        SentinelRegionIndex = State.SentinelRegionIndex,
        SentinelCellIndex = State.SentinelCellIndex,
        BreachPThresholdKpa = 10f,
        BreachFluxThresholdUmol = 50_000f,
      }.Schedule(State.RegionFaceBuffer.TotalFaceCount, 64, dependency);

      return AtmosphereManager.CompleteIfNeeded(handle);
    }
  }
}
