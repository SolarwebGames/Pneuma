using Unity.Collections;
using System;
using SolarWeb.Pneuma.Data;

namespace SolarWeb.Pneuma.Grid
{
  public class AtmosphereEvents : IDisposable
  {
    public NativeQueue<InjectionRequest> PendingInjections;
    public NativeQueue<int> IgnitionEvents;
    public NativeQueue<OverpressureEvent> OverpressureEvents;

    public void Initialize()
    {
      PendingInjections = new NativeQueue<InjectionRequest>(Allocator.Persistent);
      IgnitionEvents = new NativeQueue<int>(Allocator.Persistent);
      OverpressureEvents = new NativeQueue<OverpressureEvent>(Allocator.Persistent);
    }

    public void Dispose()
    {
      if (PendingInjections.IsCreated) PendingInjections.Dispose();
      if (IgnitionEvents.IsCreated) IgnitionEvents.Dispose();
      if (OverpressureEvents.IsCreated) OverpressureEvents.Dispose();
    }
  }
}
