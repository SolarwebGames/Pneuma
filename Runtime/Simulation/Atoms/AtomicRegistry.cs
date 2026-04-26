using System;
using System.Collections.Generic;
using System.Linq;

namespace SolarWeb.Pneuma.Atoms
{
  public class AtomicRegistry
  {
    private readonly Dictionary<string, AtomDefinition> definitions = new();

    public bool IsInitialized { get; private set; }
    public int TotalAtomCount { get; private set; }

    public void Initialize(IEnumerable<AtomDefinition> atoms)
    {
      if (!atoms.Any())
      {
        TotalAtomCount = 0;
        IsInitialized = true;
        return;
      }

      TotalAtomCount = atoms.Max(a => a.AtomicNumber) + 1;

      foreach (var def in atoms)
      {
        int idx = def.AtomicNumber - 1;
        var p = def.Properties;

        var props = def.Properties;
        var coordProfile = CoordinationProfile.GetCoordinationProfile(def.AtomicNumber, props.Group, props.Period);
        props.CoordinationProfile = coordProfile;

        def.Properties = props;

        definitions[def.Symbol] = def;
      }

      IsInitialized = true;
    }

    public AtomDefinition Get(string symbol)
    {
      if (definitions.TryGetValue(symbol, out var atom))
        return atom;

      throw new Exception($"[Pneuma]: Element '{symbol}' not found.");
    }
  }
}