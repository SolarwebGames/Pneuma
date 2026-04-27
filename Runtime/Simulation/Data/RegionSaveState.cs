namespace SolarWeb.Pneuma.Data
{
	public struct RegionSaveState
	{
		public int MinX;
		public int MinZ;
		public int MaxX;
		public int MaxZ;
		
		public long[] GasUMoles;
		public long[] SolidUMoles;
		public long[] LiquidUMoles;
		public float TemperatureK;
		public float EnclosingTemperatureK;
		public float InternalMassTemperatureK;
		public bool IsBurning;
		public float BurnIntensity;
	}
}