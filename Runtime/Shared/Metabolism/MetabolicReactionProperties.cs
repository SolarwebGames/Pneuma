using System;

namespace SolarWeb.Pneuma.Metabolism
{
  [Serializable]
  public struct MetabolicReactionProperties
  {
    public int InputId;
    public int OutputId;
    public int TargetGlobinId;

    public long TargetMetabolicRatePerSecond; // Base micromoles per second
    public long EfficiencyBP; // Basis points (1/10000th) for conversion efficiency

    /// <summary>
    /// How aggressively this reaction pulls from its compatible plasma storage.
    /// Helps O2 compete against other gases that might be in the same "Heme" globin.
    /// </summary>
    public float BindingPriority;
  }
}