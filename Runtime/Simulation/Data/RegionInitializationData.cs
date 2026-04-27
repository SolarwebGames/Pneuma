using System.Collections.Generic;

namespace SolarWeb.Pneuma.Data
{
	public struct RegionInitializationData
	{
		public int SimIndex;
		public int RoomID;
		public float TemperatureK;
		public float EnclosingTemperatureK;
		public float InternalMassTemperatureK;
		public float MaxPressureKpa;
		public List<int> RegionCells;
	}
}