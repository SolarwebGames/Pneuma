using System.Collections.Generic;
using System.IO;
using Unity.Burst;
using UnityEngine;

namespace SolarWeb.Pneuma
{
	public static class BurstAssemblyLoader
	{
		public static List<string> LoadBurstAssemblies(string folder)
		{
			var loadedAssemblies = new List<string>();
			string burstFileExtension;
			string platformFolder;

			switch (Application.platform)
			{
				case RuntimePlatform.WindowsPlayer:
				case RuntimePlatform.WindowsEditor:
					platformFolder = "Windows";
					burstFileExtension = "dll";
					break;
				case RuntimePlatform.OSXPlayer:
				case RuntimePlatform.OSXEditor:
					platformFolder = "OSX";
					burstFileExtension = "dylib";
					break;
				case RuntimePlatform.LinuxPlayer:
				case RuntimePlatform.LinuxEditor:
					platformFolder = "Linux";
					burstFileExtension = "so";
					break;
				default:
					Debug.LogWarning($"[Pneuma] Unsupported platform for Burst assembly loading: {Application.platform}. Jobs will run using default code, which may be slower.");
					return loadedAssemblies;
			}

			var platformPath = Path.Combine(folder, platformFolder);

			var files = Directory.EnumerateFiles(platformPath, $"*.{burstFileExtension}");

			foreach (var file in files)
			{
				var loaded = BurstRuntime.LoadAdditionalLibrary(file);
				if (!loaded)
				{
					Debug.LogWarning($"[Pneuma] Loading jobs from {file} failed, jobs will run using managed mode, which will impact performance.");
				}
				else
				{
					loadedAssemblies.Add(file);
					Debug.LogWarning($"[Pneuma] Loading jobs from {file} successful.");
				}
			}
			return loadedAssemblies;
		}
	}
}