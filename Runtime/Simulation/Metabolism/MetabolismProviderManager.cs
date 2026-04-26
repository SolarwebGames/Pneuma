namespace SolarWeb.Pneuma.Metabolism
{
  public static class MetabolismProviderManager
  {
    /// <summary>
    /// Called on the main thread before scheduling the Breathe job.
    /// Snapshots each provider's gas composition into ExternalLungSupply and sets the HasExternalSupply flag.
    /// Also clears ExternalNetConsumed so the job starts fresh.
    /// </summary>
    public static void DoExternalProviderPreStep(MetabolismBatch batch)
    {
      if (batch.ProviderSnapshotBuffer == null || batch.ProviderSnapshotBuffer.Length < batch.GasCount)
        batch.ProviderSnapshotBuffer = new long[batch.GasCount];

      for (int i = 0; i < batch.Count; i++)
      {
        var provider = batch.ExternalGasSource[i];

        if (provider == null || !provider.IsValid)
        {
          batch.HasExternalSupply[i] = false;
          for (int g = 0; g < batch.GasCount; g++)
          {
            int storageIdx = batch.GetGasIndex(i, g);
            batch.ExternalLungSupply[storageIdx] = 0;
            batch.ExternalNetConsumed[storageIdx] = 0;
          }
          continue;
        }

        // Single managed call fills the flat scratch buffer — no per-gas virtual dispatch.
        // Returns false when the provider is empty so the job falls back to room air.
        bool hasGas = provider.GetAllAvailableUMoles(batch.ProviderSnapshotBuffer, batch.GasCount);
        batch.HasExternalSupply[i] = hasGas;

        for (int g = 0; g < batch.GasCount; g++)
        {
          int storageIdx = batch.GetGasIndex(i, g);
          batch.ExternalLungSupply[storageIdx] = hasGas ? batch.ProviderSnapshotBuffer[g] : 0;
          batch.ExternalNetConsumed[storageIdx] = 0;
        }
      }
    }

    /// <summary>
    /// Called on the main thread after the Breathe job has completed.
    /// Reads ExternalNetConsumed and drains/refills the provider accordingly.
    /// Positive = consumed from provider (ExtractGas); negative = returned to provider (InjectGas, e.g. exhaled CO2).
    /// </summary>
    public static void DoExternalProviderPostStep(MetabolismBatch batch)
    {
      for (int i = 0; i < batch.Count; i++)
      {
        if (!batch.HasExternalSupply[i]) continue;
        var provider = batch.ExternalGasSource[i];
        if (provider == null || !provider.IsValid) continue;

        for (int g = 0; g < batch.GasCount; g++)
        {
          int storageIdx = batch.GetGasIndex(i, g);
          long net = batch.ExternalNetConsumed[storageIdx];
          if (net > 0)
            provider.ExtractGas(g, net);       // consumed from tank
          else if (net < 0 && batch.ExternalLungSupply[storageIdx] > 0)
            provider.InjectGas(g, -net);       // supply gas recycled back to tank
          // Waste gases (net < 0, not in supply) were already vented to room by the Breathe job
        }
      }
    }
  }
}
