namespace SolarWeb.Pneuma.Data
{
  public struct StructuralProperties
  {
    public float ThermalCapacity;
    public float ThermalConductivity;
    public float FaceGasPermeability;
    public float Volume;
    public float MaxPressureDeltaKpa;

		public static StructuralProperties Empty => new()
		{
			Volume = 0f,
			ThermalCapacity = 0.0001f,
			FaceGasPermeability = 1,
			ThermalConductivity = 0.065f,
			MaxPressureDeltaKpa = 0f,
		};


		public static StructuralProperties DefaultWall => new()
    {
      ThermalCapacity = 2500.0f,
      ThermalConductivity = 2.2f,
      FaceGasPermeability = 0f,
      Volume = 2.4f,
      MaxPressureDeltaKpa = 100f,
    };

		public static StructuralProperties DefaultBuildingPassable => new()
		{
			ThermalCapacity = 5000,
			ThermalConductivity = 0.065f,
			FaceGasPermeability = 0.5f,
			Volume = 1.25f,
			MaxPressureDeltaKpa = float.MaxValue,
		};

		public static StructuralProperties DefaultBuildingImpassable => new()
		{
			ThermalCapacity = 2500.0f,
			ThermalConductivity = 2.5f,
			FaceGasPermeability = 0f,
			Volume = 2.3f,
			MaxPressureDeltaKpa = 500f,
		};
	}
}