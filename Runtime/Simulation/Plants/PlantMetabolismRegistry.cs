using SolarWeb.Pneuma.Data;
using System;
using Unity.Collections;

namespace SolarWeb.Pneuma.Grid
{
  /// <summary>
  /// Universal definitions for different plant metabolic profiles.
  /// Each profile defines stoichiometric gas-formula reactions and hazard tolerance.
  /// </summary>
  public class PlantMetabolismRegistry : IDisposable
  {
    public const int MaxReagentsPerProfile = 4;

    // --- Reaction inputs: [profileIdx * MaxReagentsPerProfile + slot] ---
    /// <summary>Gas IDs for each input slot. -1 = unused.</summary>
    public NativeArray<int> InputGasIds;
    public NativeArray<float> InputMolarRatios;
    public NativeArray<float> InputMinPressuresPa;
    public NativeArray<bool> InputIsRoot;

    // --- Reaction outputs: [profileIdx * MaxReagentsPerProfile + slot] ---
    /// <summary>Gas IDs for each output slot. -1 = unused.</summary>
    public NativeArray<int> OutputGasIds;
    public NativeArray<float> OutputMolarRatios;
    public NativeArray<bool> OutputIsRoot;

    /// <summary>Number of active input slots per profile.</summary>
    public NativeArray<int> InputCounts;
    /// <summary>Number of active output slots per profile.</summary>
    public NativeArray<int> OutputCounts;

    /// <summary>Multiplier for growth based on atmospheric radioactivity (Radiotrophs = positive).</summary>
    public NativeArray<float> RadiationAffinity;

    /// <summary>Sensitivity to chemical hazards (Corrosiveness/BioInterference).</summary>
    public NativeArray<float> ChemicalTolerance;

    /// <summary>Per-profile effective Corrosiveness, indexed [profileIdx * GasCount + gasId].
    /// Gases scoring below hazardThreshold are zeroed so they contribute no chemical stress.</summary>
    public NativeArray<float> EffectiveCorrosiveness;

    /// <summary>Per-profile effective BioInterference, indexed [profileIdx * GasCount + gasId].</summary>
    public NativeArray<float> EffectiveBioInterference;

    public int ProfileCount;
    public int GasCount;

    public void Initialize(int profileCount)
    {
      ProfileCount = profileCount;
      int slots = profileCount * MaxReagentsPerProfile;
      InputGasIds.Resize(slots);
      InputMolarRatios.Resize(slots);
      InputMinPressuresPa.Resize(slots);
      InputIsRoot.Resize(slots);
      OutputGasIds.Resize(slots);
      OutputMolarRatios.Resize(slots);
      OutputIsRoot.Resize(slots);
      InputCounts.Resize(profileCount);
      OutputCounts.Resize(profileCount);
      RadiationAffinity.Resize(profileCount);
      ChemicalTolerance.Resize(profileCount);
      // Mark all slots as unused (-1); NativeArray zero-initialises by default
      for (int i = 0; i < slots; i++) { InputGasIds[i] = -1; OutputGasIds[i] = -1; }
    }

    public void Dispose()
    {
      InputGasIds.SafeDispose();
      InputMolarRatios.SafeDispose();
      InputMinPressuresPa.SafeDispose();
      InputIsRoot.SafeDispose();
      OutputGasIds.SafeDispose();
      OutputMolarRatios.SafeDispose();
      OutputIsRoot.SafeDispose();
      InputCounts.SafeDispose();
      OutputCounts.SafeDispose();
      RadiationAffinity.SafeDispose();
      ChemicalTolerance.SafeDispose();
      EffectiveCorrosiveness.SafeDispose();
      EffectiveBioInterference.SafeDispose();
    }
  }
}
