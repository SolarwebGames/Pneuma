using Unity.Jobs;
using SolarWeb.Pneuma.Grid;
using SolarWeb.Pneuma.Jobs.Diffusion;
using SolarWeb.Pneuma.Logging;

namespace SolarWeb.Pneuma.Simulation
{
  public class RegionOverpressure
  {
    private AtmosphereGrid State => manager.State;
    private readonly AtmosphereManager manager;

    public RegionOverpressure(AtmosphereManager manager)
    {
      this.manager = manager;
    }

    public JobHandle DoDetectOverpressure(JobHandle dependency = default)
    {
      JobHandle handle = dependency; if (State.SimRegionCount > 0)
      {
        handle = new DetectRegionOverpressure
        {
          PressureKpa = State.RegionGasComposition.PressureKpa,
          MaxPressureKpa = State.RegionPhysicsBuffer.MaxPressureKpa,
          OverpressureEvents = State.Events.OverpressureEvents.AsParallelWriter()
        }.Schedule(State.RegionStates.ActiveIndices, 32, handle);
      }

      if (State.RegionFaceBuffer.TotalFaceCount > 0)
      {
        handle = new DetectFaceOverpressure
        {
          FaceRegionA = State.RegionFaceBuffer.FaceRegionA,
          FaceRegionB = State.RegionFaceBuffer.FaceRegionB,
          PressureKpa = State.RegionGasComposition.PressureKpa,
          FaceMaxPressureDeltaKpa = State.RegionFaceBuffer.FaceMaxPressureDeltaKpa,
          SentinelRegionIndex = State.SentinelRegionIndex,
          OverpressureEvents = State.Events.OverpressureEvents.AsParallelWriter()
        }.Schedule(State.RegionFaceBuffer.TotalFaceCount, 64, handle);
      }

      return AtmosphereManager.CompleteIfNeeded(handle);
    }
  }
}