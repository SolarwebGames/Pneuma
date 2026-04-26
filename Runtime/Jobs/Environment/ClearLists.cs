using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace SolarWeb.Pneuma.Jobs.Environment
{
  [BurstCompile]
  public struct ClearLists : IJob
  {
    public NativeList<int> Cells;
    public NativeList<int> Faces;
    public void Execute()
    {
      Cells.Clear();
      Faces.Clear();
    }
  }
}
