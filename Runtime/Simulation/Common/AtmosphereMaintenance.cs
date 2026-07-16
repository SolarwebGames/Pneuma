using SolarWeb.Pneuma.Grid;
using SolarWeb.Pneuma.Jobs.Sync;
using Unity.Jobs;

namespace SolarWeb.Pneuma.Simulation
{
  public class AtmosphereMaintenance
  {
    private AtmosphereGrid State => manager.State;
    private readonly AtmosphereManager manager;

    public AtmosphereMaintenance(AtmosphereManager manager)
    {
      this.manager = manager;
    }

    /// <summary>
    /// Re-copies ambient gas to the sentinel region when the ambient atmosphere changes
    /// (e.g. season/biome update). Runs on the main thread; no job overhead needed for
    /// a single sentinel slot.
    /// </summary>
    public JobHandle DoSyncAmbientToGrid(JobHandle dependency = default)
    {
      dependency.Complete();
      var env = State.AmbientEnvironment;
      for (int g = 0; g < State.GasCount; g++)
      {
        var regionIndex = State.GetRegionUMoleIndex(g, State.SentinelRegionIndex);
        State.RegionGasComposition.uMoles[regionIndex] = env.uMolesInOneCell[g];
      }
      State.RegionGasComposition.TotalUMoles[State.SentinelRegionIndex] = env.TotalUMolesInOneCell;
      State.RegionGasComposition.PressureKpa[State.SentinelRegionIndex] = env.TotalPressureKpa;
      State.RegionPhysicsBuffer.TemperatureK[State.SentinelRegionIndex] = env.TemperatureK;
      State.RegionPhysicsBuffer.EnclosingTemperatureK[State.SentinelRegionIndex] = env.TemperatureK;
      State.RegionPhysicsBuffer.InternalMassTemperatureK[State.SentinelRegionIndex] = env.TemperatureK;
      return default;
    }

    public JobHandle DoCalculateRoomTemperatureDeltas(JobHandle dependency = default)
    {
      var handle = new CalculateRoomTemperatureDeltas
      {
        RegionToRoomIndex = State.RegionStates.RegionToRoomIndex,
        TemperatureK = State.RegionPhysicsBuffer.TemperatureK,
        RegionVolumes = State.RegionPhysicsBuffer.RegionVolumes,
        RoomCurrentTemperatureK = State.RegionPhysicsBuffer.RoomCurrentTemperatureK,
        RegionRoomTempDeltas = State.RegionStates.RegionRoomTempDeltas,
        ActiveTicks = State.RegionStates.ActiveTicks,
        SentinelRegionIndex = State.SentinelRegionIndex,
        HysteresisTicks = 10
      }.Schedule(State.SimRegionCount, 64, dependency);

      return AtmosphereManager.CompleteIfNeeded(handle);
    }

    public JobHandle DoAggregateRoomData(JobHandle dependency = default)
    {
      var handle = new AggregateRoomData
      {
        RegionToRoomIndex = State.RegionStates.RegionToRoomIndex,
        RegionVolumes = State.RegionPhysicsBuffer.RegionVolumes,
        RegionPressureKpa = State.RegionGasComposition.PressureKpa,
        RegionRoomTempDeltas = State.RegionStates.RegionRoomTempDeltas,

        FaceRegionA = State.RegionFaceBuffer.FaceRegionA,
        FaceRegionB = State.RegionFaceBuffer.FaceRegionB,
        FaceMaxPressureDeltaKpa = State.RegionFaceBuffer.FaceMaxPressureDeltaKpa,

        RoomWeightedPressure = State.RegionStates.RoomWeightedPressure,
        RoomTotalVolume = State.RegionStates.RoomTotalVolume,
        RoomWeightedTempDelta = State.RegionStates.RoomWeightedTempDelta,
        RoomMinPressureRating = State.RegionStates.RoomMinPressureRating,
        RoomMaxCurrentDeltaP = State.RegionStates.RoomMaxCurrentDeltaP,

        SimRegionCount = State.SimRegionCount,
        TotalFaceCount = State.RegionFaceBuffer.TotalFaceCount,
        RoomCount = State.RegionStates.RoomCount,
        SentinelRegionIndex = State.SentinelRegionIndex
      }.Schedule(dependency);

      return AtmosphereManager.CompleteIfNeeded(handle);
    }
  }
}
