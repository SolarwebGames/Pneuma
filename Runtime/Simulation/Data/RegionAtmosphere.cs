using System.Collections.Generic;

namespace SolarWeb.Pneuma.Data
{
	public class RegionAtmosphere
	{
		public int RegionId;
		public float TotalVolume;
		public long[] TotalUMoles = System.Array.Empty<long>();
		public List<int> ConnectedRegionIds = new List<int>();
		public bool IsExposedToOutdoors;
	}
}