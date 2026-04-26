namespace SolarWeb.Pneuma.Data
{
  public struct CellInitializationData
  {
    public int WorldIndex;
    public WorldPos WorldPos;
		public int RegionIndex;
		public StructuralProperties CellProperties;
    public StructuralProperties Top;
    public StructuralProperties Bottom;
    public float GasCapacityMask;
  }
}