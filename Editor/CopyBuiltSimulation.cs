using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

namespace SolarWeb.Pneuma.Editor
{
	public static class CopyBuiltSimulation
	{
		[PostProcessBuild]
		public static void OnPostBuild(BuildTarget target, string pathToBuiltProject)
		{
			List<string> managedFiles = new()
			{
				"SolarWeb.Pneuma.Jobs",
				"SolarWeb.Pneuma.Shared",
				"SolarWeb.Pneuma.Simulation"
			};

			var burstPlatformFolderTarget = "";
			var burstExtension = "";
			var burstSourceFolder = "";
			switch (target)
			{
				case BuildTarget.StandaloneWindows64:
					burstExtension = "dll";
					burstPlatformFolderTarget = "Windows";
					burstSourceFolder = "x86_64";
					break;
				case BuildTarget.StandaloneLinux64:
					burstExtension = "so";
					burstPlatformFolderTarget = "Linux";
					break;
				case BuildTarget.StandaloneOSX:
					burstPlatformFolderTarget = "OSX";
					burstExtension = "dylib";
					break;
			}

			var settingsAssets = AssetDatabase.FindAssets("t:PneumaSettings");
			if (settingsAssets.Length == 0)
			{
				Debug.LogWarning("No PneumaSettings asset found. Built assets will not be copied.");
				return;
			}

			var settings = AssetDatabase.LoadAssetAtPath<PneumaSettings>(AssetDatabase.GUIDToAssetPath(settingsAssets[0]));

			var baseTargetPath = settings.OutputPath;
			var assembliesTargetPath = Path.Combine(baseTargetPath, settings.AssembliesFolder);
			var burstTargetPath = Path.Combine(baseTargetPath, settings.BurstFolder, burstPlatformFolderTarget);
			var dataFolder = $"{PlayerSettings.productName}_Data";

			var basePath = Path.Combine(Path.GetDirectoryName(pathToBuiltProject), dataFolder);
			var burstSource = Path.Combine(basePath, "Plugins", burstSourceFolder, $"lib_burst_generated.{burstExtension}");
			var managedPath = Path.Combine(basePath, "Managed");

			foreach (var file in managedFiles)
			{
				Debug.Log($"Copying {file} from {managedPath} to {assembliesTargetPath}");
				File.Copy(Path.Combine(managedPath, $"{file}.dll"), Path.Combine(assembliesTargetPath, $"{file}.dll"), true);
				if (settings.CopyPDBs)
				{
					File.Copy(Path.Combine(managedPath, $"{file}.pdb"), Path.Combine(assembliesTargetPath, $"{file}.pdb"), true);
				}
			}

			var burstFileTarget = Path.Combine(burstTargetPath, $"PneumaJobs.{burstExtension}");

			Debug.Log($"Copying Burst file from {burstSource} to {burstFileTarget}");
			File.Copy(burstSource, burstFileTarget, true);
		}
	}
}