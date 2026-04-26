using System;

namespace SolarWeb.Pneuma.Atoms
{
  [Serializable]
  public struct CoordinationProfile
  {
    /// <summary>
    /// The geometry the atom adopts by default (e.g., in organic molecules).
    /// Carbon=4, Nitrogen=3, Oxygen=2, Hydrogen=1.
    /// </summary>
    public int DefaultCoordination;

    /// <summary>
    /// The absolute hard limit of ligands this atom can support.
    /// Period 2 elements (C, N, O) are capped at 4.
    /// Period 3+ (Si, P, S) can expand to 6 or higher (Hypervalent).
    /// </summary>
    public int MaxCoordination;

    /// <summary>
    /// 0 = Standard (s/p orbitals only).
    /// 1 = Hypervalent (Access to d-orbitals, can expand CN).
    /// 2 = Electron Deficient (Boron, often forms bridges).
    /// </summary>
    public int BehaviorType;

    public readonly bool IsGeometryPossible(int targetCN) => targetCN <= MaxCoordination;

    public static CoordinationProfile GetCoordinationProfile(int atomicNumber, int group, int period)
    {
      // 1. NOBLE GASES (Group 18)
      if (group == 18)
      {
        // Helium/Neon are inert. Heavier ones (Kr, Xe) can bond.
        if (period < 4) return new CoordinationProfile { DefaultCoordination = 0, MaxCoordination = 0 };
        return new CoordinationProfile { DefaultCoordination = 0, MaxCoordination = 6, BehaviorType = 1 };
      }

      // 2. HYDROGEN (Special Case)
      if (atomicNumber == 1) return new CoordinationProfile { DefaultCoordination = 1, MaxCoordination = 1 };

      // 3. PERIOD 2 (Strict Octet Limit)
      if (period == 2)
      {
        if (group == 1) return new CoordinationProfile { DefaultCoordination = 1, MaxCoordination = 4 }; // Li
        if (group == 2) return new CoordinationProfile { DefaultCoordination = 2, MaxCoordination = 4 }; // Be
        if (group >= 13 && group <= 17)
        {
          // B=3, C=4, N=3, O=2, F=1
          int def = (group == 13) ? 3 : (group == 14) ? 4 : (18 - group);
          // C/N/O can reach 4. F is strictly 1.
          int max = (group == 17) ? 1 : 4;
          return new CoordinationProfile { DefaultCoordination = def, MaxCoordination = max };
        }
      }

      // 4. TRANSITION METALS (Groups 3-12)
      if (group >= 3 && group <= 12)
      {
        // General rule: Octahedral (6) is king.
        int def = 6;

        // Late transition metals (Cu, Zn, Ag, Au, Cd, Hg) often prefer 4
        if (group >= 11) def = 4;

        return new CoordinationProfile { DefaultCoordination = def, MaxCoordination = 9, BehaviorType = 1 };
      }

      // 5. MAIN GROUP HEAVY (Period 3+)
      if (group >= 13 && group <= 17)
      {
        int def = (group == 13) ? 3 : (group == 14) ? 4 : (18 - group);
        // Can expand octet (d-orbitals)
        return new CoordinationProfile { DefaultCoordination = def, MaxCoordination = 6, BehaviorType = 1 };
      }

      // 6. F-BLOCK (Lanthanides/Actinides)
      if (atomicNumber >= 57 && atomicNumber <= 71 || atomicNumber >= 89 && atomicNumber <= 103)
      {
        return new CoordinationProfile { DefaultCoordination = 9, MaxCoordination = 12, BehaviorType = 1 };
      }

      // Fallback
      return new CoordinationProfile { DefaultCoordination = 1, MaxCoordination = 6 };
    }
  }
}