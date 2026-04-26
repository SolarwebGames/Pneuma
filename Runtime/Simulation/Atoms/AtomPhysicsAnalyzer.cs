using System;

namespace SolarWeb.Pneuma.Atoms
{
  public static class AtomPhysicsAnalyzer
  {

    public static bool CanFormCoordinateBond(AtomicRegistry registry, string gasAtomSymbol, string targetElementSymbol)
    {
      var gasAtom = registry.Get(gasAtomSymbol);
      var target = registry.Get(targetElementSymbol);

      float deltaChi = Math.Abs(gasAtom.Properties.Electronegativity_Pauling - target.Properties.Electronegativity_Pauling);

      // 1.0 - 2.2 is the typical range for coordinate covalent ligands (O2, CO to Fe)
      bool electronegativityMatch = (deltaChi >= 1.0f && deltaChi <= 2.2f);

      bool isLigand = IsLigand(gasAtomSymbol);

      return electronegativityMatch && isLigand;
    }

    // Only specific atoms typically act as donors (N, O, C, S, P, Halogens)
    private static bool IsLigand(string symbol)
    {
      return symbol is "O" or "N" or "C" or "S" or "P" or "F" or "Cl" or "Br" or "I";
    }

  }
}