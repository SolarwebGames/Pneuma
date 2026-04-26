using System;

namespace SolarWeb.Pneuma.Metabolism
{
  public interface IMetabolismBatch : IDisposable
  {
    int MetabolismId { get; }
    int DangerBufferIndex { get; }
    int Count { get; }
    bool IsSimplified { get; }
    int AddEntity(IMetabolizer metabolizer);
    void RemoveEntity(int entityIdx);
    void UpdatePosition(IMetabolizer metabolizer);
    float GetDangerLevel(int regionIdx, int simIdx = -1);
    void UpdateEfficiencies(int entityIdx, float breathing, float pumping, float filtration);
    void UpdateResistances(int entityIdx, float toxic, float corrosive, float radiation, float pressure, float vacuum);
  }
}
