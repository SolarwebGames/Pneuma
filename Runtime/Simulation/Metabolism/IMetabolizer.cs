using SolarWeb.Pneuma.GasExchange;

namespace SolarWeb.Pneuma.Metabolism
{

  public interface IMetabolizer : IExchanger
  {
    public int BatchIndex { get; set; }
    public IMetabolismBatch? BatchReference { get; set; }
    public void OnStageTriggered(int subscriptionIndex);
    public void OnStageReset(int subscriptionIndex);
    public float BreathStrength { get; }
  }
}