using SolarWeb.Pneuma.Constants;

namespace SolarWeb.Pneuma.MathA
{
	public static class AtmosphereCalc
	{
		public static double IdealGasLaw(double pressurePa, double cellVolume, double temperature)
		{
			return (pressurePa * cellVolume) / (AtmosphereConstants.R * temperature);
		}

		/// <summary>
		/// Calculates a multiplier for heat capacity that increases as temperature rises.
		/// This models the increased energy required to excite high-energy states at extreme temperatures.
		/// At 100°C (373.15K) and below, it is 1.0. At 50,000°C, it is approximately 50.0.
		/// </summary>
		public static float GetTemperatureCapacityFactor(float tempK)
		{
			float tempC = tempK - 273.15f;
			if (tempC <= 100f) return 1.0f;
			return 1.0f + (tempC - 100f) / 1000f;
		}
	}
}
