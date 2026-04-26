using System;

namespace SolarWeb.Pneuma.Metabolism
{
  [Serializable]
  public struct MetabolicReaction
  {
    public string InputFormula;
    public string OutputFormula;
    public string RequiredGlobinName;
    public MetabolicReactionProperties Properties;
  }
}