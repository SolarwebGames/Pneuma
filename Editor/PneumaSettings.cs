using UnityEngine;

namespace SolarWeb.Pneuma.Editor
{
	[CreateAssetMenu(fileName = "PneumaSettings.asset", menuName = "Pneuma/Settings")]
	public class PneumaSettings : ScriptableObject
	{
		[field: SerializeField]
		public string OutputPath { get; private set; }

		[field: SerializeField]
		public string AssembliesFolder { get; private set; }

		[field: SerializeField]
		public string BurstFolder { get; private set; }

		[field: SerializeField]
		public bool CopyPDBs { get; private set; }
	}
}
