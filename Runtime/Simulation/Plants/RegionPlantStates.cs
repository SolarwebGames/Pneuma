using System;
using Unity.Collections;

namespace SolarWeb.Pneuma.Grid
{
  /// <summary>
  /// Output states calculated by the simulation for plant populations.
  /// Consumed by the mod layer to apply growth modifiers and damage.
  /// Parallel to RegionPlantPopulation SoA.
  /// </summary>
  public class RegionPlantStates : IDisposable
  {
    public NativeList<float> Efficiency;
    public NativeList<float> ChemicalStress;
    public NativeList<float> RadiologicalStress;

    public void Initialize(int regionStride, int profileCount)
    {
      int initialCapacity = regionStride * profileCount;
      Efficiency = new NativeList<float>(initialCapacity, Allocator.Persistent);
      ChemicalStress = new NativeList<float>(initialCapacity, Allocator.Persistent);
      RadiologicalStress = new NativeList<float>(initialCapacity, Allocator.Persistent);
    }

    public void Dispose()
    {
      if (Efficiency.IsCreated) Efficiency.Dispose();
      if (ChemicalStress.IsCreated) ChemicalStress.Dispose();
      if (RadiologicalStress.IsCreated) RadiologicalStress.Dispose();
    }
  }
}
