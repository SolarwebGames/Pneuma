namespace SolarWeb.Pneuma.GasExchange
{
  public interface IExchanger
  {
    public int Position { get; }
    public int Region { get; }
    public bool IsActive { get; }
    public int Id { get; set; }
  }
}