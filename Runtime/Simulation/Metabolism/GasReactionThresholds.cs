using System.Collections.Generic;

namespace SolarWeb.Pneuma.Metabolism
{
  public class GasReactionThresholds
  {
    public int GasId;
    public string Formula = string.Empty;
    public bool IsAbsence;
    public bool NegativeReaction = false;
    public bool TracksLungLevel = false;
    public List<GasThresholdStage> Stages = new();
  }
}