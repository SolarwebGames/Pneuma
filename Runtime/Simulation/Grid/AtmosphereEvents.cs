using Unity.Collections;
using System;
using SolarWeb.Pneuma.Data;

namespace SolarWeb.Pneuma.Grid
{
  public class AtmosphereEvents : IDisposable
  {
    public NativeQueue<InjectionRequest> PendingInjections;

    // Fixed-capacity lists rather than NativeQueue: these are written via ParallelWriter.AddNoResize
    // from Burst jobs, and drained/Cleared once per tick on the main thread. NativeQueue's block-pool
    // growth path (allocating a second block once the first fills) triggers a reproducible native
    // crash under Burst AOT compilation the first time it's exercised; fixed-capacity lists sized to
    // the true worst case (one event per region/face) avoid that code path entirely.
    public NativeList<int> IgnitionEvents;
    public NativeList<OverpressureEvent> OverpressureEvents;

    public void Initialize(int regionCapacity, int faceCapacity)
    {
      PendingInjections = new NativeQueue<InjectionRequest>(Allocator.Persistent);
      IgnitionEvents = new NativeList<int>(regionCapacity, Allocator.Persistent);
      OverpressureEvents = new NativeList<OverpressureEvent>(regionCapacity + faceCapacity, Allocator.Persistent);
    }

    public void Dispose()
    {
      if (PendingInjections.IsCreated) PendingInjections.Dispose();
      if (IgnitionEvents.IsCreated) IgnitionEvents.Dispose();
      if (OverpressureEvents.IsCreated) OverpressureEvents.Dispose();
    }
  }
}
