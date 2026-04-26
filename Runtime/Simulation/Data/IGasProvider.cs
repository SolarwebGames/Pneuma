namespace SolarWeb.Pneuma.Data
{
	public interface IGasProvider
	{
		string ProviderLabel { get; }
		int Priority { get; }

		float ExtractGas(int gasId, long micromoles);
		void InjectGas(int gasId, long micromoles);
	}
}