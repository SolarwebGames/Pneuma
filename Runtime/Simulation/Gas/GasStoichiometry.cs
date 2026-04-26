using SolarWeb.Pneuma.Data;
using System;
using Unity.Collections;
using Unity.Mathematics;

namespace SolarWeb.Pneuma.Grid
{
  /// <summary>
  /// Generalized stoichiometric mapping for all gases in the simulation.
  /// Stores constituent atom counts per molecule for any defined element.
  /// Indexed as: gasId * TotalAtomCount + atomIndex (where atomIndex is AtomicNumber).
  /// </summary>
  public class GasStoichiometry : IDisposable
  {
    public NativeArray<float> AtomicWeights;

    /// <summary>
    /// Bitmask of present atomic numbers per gas.
    /// Each uint4 covers 128 elements.
    /// </summary>
    public NativeArray<uint4> AtomicMasks;

    public int TotalAtomCount;
    public int GasCount;

    public void Initialize(int gasCount, int totalAtomCount)
    {
      GasCount = gasCount;
      TotalAtomCount = totalAtomCount;

      AtomicWeights.Resize(gasCount * totalAtomCount);
      AtomicMasks.Resize(gasCount);
    }

    public void Dispose()
    {
      AtomicWeights.SafeDispose();
      AtomicMasks.SafeDispose();
    }
  }
}
