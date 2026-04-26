using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace SolarWeb.Pneuma.Atoms
{
  public static class Formulas
  {
    // Group 1: Symbol, Group 2: [Isotope], Group 3: Count
    private static readonly Regex formulaRegex = new(@"([A-Z][a-z]*)(\[\d+\])?(\d*)", RegexOptions.Compiled);

    public static string Normalize(string formula, AtomicRegistry registry, Dictionary<string, AtomCountPair>? parsedData = null)
    {
      if (string.IsNullOrWhiteSpace(formula)) throw new Exception("Attempting to parse empty formula");

      if (parsedData == null) parsedData = ParseFormula(formula, registry);
      return BuildHillString(parsedData);
    }

    public static Dictionary<string, AtomCountPair> ParseFormula(string formula, AtomicRegistry registry)
    {
      var results = new Dictionary<string, AtomCountPair>();
      var matches = formulaRegex.Matches(formula);

      foreach (Match match in matches)
      {
        string symbol = match.Groups[1].Value;
        string isotopeStr = match.Groups[2].Value;
        string countStr = match.Groups[3].Value;

        int count = string.IsNullOrEmpty(countStr) ? 1 : int.Parse(countStr);

        // Create a lookup key that includes the isotope if present (e.g., "C14" or "C")
        string lookupSymbol = symbol + isotopeStr;
        AtomDefinition def = registry.Get(lookupSymbol);

        if (def != null)
        {
          if (results.ContainsKey(lookupSymbol))
          {
            results[lookupSymbol] = new AtomCountPair
            {
              Definition = def,
              Count = results[lookupSymbol].Count + count
            };
          }
          else
          {
            results.Add(lookupSymbol, new AtomCountPair { Definition = def, Count = count });
          }
        }
        else
        {
          throw new Exception($"Formula {formula} contains unknown element/isotope: {lookupSymbol}");
        }
      }

      return results;
    }

    private static string BuildHillString(Dictionary<string, AtomCountPair> atoms)
    {
      static void AppendAtom(StringBuilder sb, AtomDefinition def, int count)
      {
        sb.Append(def.Symbol);
        if (count > 1) sb.Append(count);
      }

      var sb = new StringBuilder();
      var symbols = new List<string>(atoms.Keys);

      // Hill logic: Carbon isotopes first, then Hydrogen isotopes
      // We look for any key starting with 'C' (like C or C14)
      var carbonKeys = symbols.FindAll(s => s.StartsWith("C") && (s.Length == 1 || char.IsDigit(s[1])));
      carbonKeys.Sort(); // Keep C before C14 if both present
      foreach (var key in carbonKeys)
      {
        AppendAtom(sb, atoms[key].Definition, atoms[key].Count);
        symbols.Remove(key);
      }

      var hydrogenKeys = symbols.FindAll(s => s.StartsWith("H") && (s.Length == 1 || char.IsDigit(s[1])));
      hydrogenKeys.Sort();
      foreach (var key in hydrogenKeys)
      {
        AppendAtom(sb, atoms[key].Definition, atoms[key].Count);
        symbols.Remove(key);
      }

      // Alphabetical for the rest
      symbols.Sort();
      foreach (var symbol in symbols)
      {
        AppendAtom(sb, atoms[symbol].Definition, atoms[symbol].Count);
      }

      return sb.ToString();
    }

    public struct AtomCountPair
    {
      public AtomDefinition Definition;
      public int Count;
    }
  }
}