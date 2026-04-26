using System.Collections.Generic;

namespace SolarWeb.Pneuma.GasExchange
{
  public struct ExchangeRequest
  {
    public int CellIndex;
    public int GasId;
    public long Amount;
    public int ExchangerIndex;
  }

  // Comparison for sorting by CellIndex
  public struct RequestComparer : IComparer<ExchangeRequest>
  {
    public readonly int Compare(ExchangeRequest x, ExchangeRequest y) => x.CellIndex.CompareTo(y.CellIndex);
  }
}