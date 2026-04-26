namespace SolarWeb.Pneuma.Data
{
	public class RegionFaceLink
	{
		public int RegionA;
		public int RegionB;
		public byte FaceType;
		public float TotalPermeability;
		public float TotalConductance;
		public float TotalSurfaceArea;

		public float MinCollisionDiameter = 0f;
		public float MaxCollisionDiameter = float.MaxValue;

		public Unity.Mathematics.int3 Direction;
		public sbyte FlowDirection = 0;
		public float ActivePumpRate = 0f;
		/// <summary>
		/// Maximum pressure differential (kPa) the pump can overcome. At this back-pressure the
		/// effective pump rate reaches zero via linear falloff. 0 = unlimited.
		/// </summary>
		public float MaxPumpPressureKpa = 0f;
		public float RegulatorKpa = -1f;
		public float MaxPressureDeltaKpa = float.MaxValue;

		public float WindExposureX = 0f;
		public float WindExposureZ = 0f;

		public bool IsSpecializedLink =>
				MinCollisionDiameter > 0f ||
				MaxCollisionDiameter < float.MaxValue ||
				FlowDirection != 0 ||
				ActivePumpRate != 0f;
	}
}