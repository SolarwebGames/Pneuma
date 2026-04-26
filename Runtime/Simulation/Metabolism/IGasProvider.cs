namespace SolarWeb.Pneuma.Metabolism
{
  public interface IGasProvider
  {
    string ProviderLabel { get; }
    int Priority { get; }
    bool IsValid { get; }

    /// <summary>Non-consuming query — how many µmol of gasId are available.</summary>
    long GetAvailableUMoles(int gasId);

    /// <summary>
    /// Bulk non-consuming snapshot. Fills <paramref name="buffer"/> with available µmol for each
    /// gas index [0, gasCount). Resolves any internal state chain once rather than per-gas.
    /// Returns true if the total across all gases is &gt; 0.
    /// </summary>
    bool GetAllAvailableUMoles(long[] buffer, int gasCount);

    float ExtractGas(int gasId, float amountRequested);
    void InjectGas(int gasId, float amountToInject);
  }
}