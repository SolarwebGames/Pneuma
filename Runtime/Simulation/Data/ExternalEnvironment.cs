using SolarWeb.Pneuma.Gas;
using SolarWeb.Pneuma.Grid;
using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Xml.Serialization;
using Unity.Collections;

namespace SolarWeb.Pneuma.Data
{
	public class ExternalEnvironment : IDisposable
	{
		public struct GasProportion
		{
			public int GasId;
			public string? Formula;
			public float Proportion;

			public GasProportion(string? formula = null)
			{
				GasId = -1;
				Formula = formula;
				Proportion = 0;
			}

			public GasProportion(int gasId = -1)
			{
				GasId = gasId;
				Formula = null;
				Proportion = 0;
			}
		}

		public string Name = string.Empty;
		public float TemperatureK;
		public float TotalPressureKpa;
		public List<GasProportion> GasProportions = new();

		[IgnoreDataMember, XmlIgnore]
		public NativeArray<float> GasProportionsById;
		[IgnoreDataMember, XmlIgnore]
		public NativeArray<long> uMolesInOneCell;
		public long TotalUMolesInOneCell;
		public uint MoleculeMask;

		private const float CellVolume = 2.5f; // 1 * 1 * 2.5 meters

		public void Bake(GasRegistry gasRegistry, Allocator allocator = Allocator.Persistent)
		{
			if (TotalPressureKpa == 0 || TemperatureK == 0)
			{
				throw new Exception($"Total pressure and temperature must be above 0.");
			}


			if (GasProportionsById.IsCreated) GasProportionsById.Dispose();
			if (uMolesInOneCell.IsCreated) uMolesInOneCell.Dispose();
			MoleculeMask = 0;

			int totalGasCount = gasRegistry.GasCount;

			GasProportionsById = new NativeArray<float>(totalGasCount, allocator);
			uMolesInOneCell = new NativeArray<long>(totalGasCount, allocator);

			for (int i = 0; i < GasProportions.Count; i++)
			{
				var gasProportion = GasProportions[i];
				if (!string.IsNullOrEmpty(gasProportion.Formula))
				{
					gasProportion.GasId = gasRegistry.GetGasFromFormula(gasProportion.Formula!).Id;
					GasProportions[i] = gasProportion;
				}
			}

			for (int i = 0; i < totalGasCount; i++)
			{
				uMolesInOneCell[i] = 0;
			}

			for (int g = 0; g < gasRegistry.GasCount; g++)
			{
				var proportionIndex = GasProportions.FindIndex(x => x.GasId == g);
				if (proportionIndex > -1)
				{
					var proportion = GasProportions[proportionIndex];
					GasProportionsById[g] = proportion.Proportion;
				}
			}

			long totalMicromoles = 0;
			double pressurePa = TotalPressureKpa * 1000.0;
			if (TotalPressureKpa > 0 && TemperatureK > 0)
			{
				double moles = MathA.AtmosphereCalc.IdealGasLaw(pressurePa, CellVolume, TemperatureK);
				totalMicromoles += (long)(moles * 1_000_000.0);
			}

			TotalUMolesInOneCell = totalMicromoles;

			for (int i = 0; i < GasProportionsById.Length; i++)
			{
				uMolesInOneCell[i] = (long)(totalMicromoles * GasProportionsById[i]);
				if (uMolesInOneCell[i] > 0)
				{
					MoleculeMask |= (uint)(1 << i);
				}
			}
		}

		public static void CopyToGrid(ExternalEnvironment environment, AtmosphereGrid grid, GasRegistry gasRegistry)
		{
			float initialMassCapacity = 0;
			for (int g = 0; g < grid.GasCount; g++)
			{
				long molesUMol = environment.uMolesInOneCell[g];
				var gas = gasRegistry.AllGases[g];
				initialMassCapacity += (molesUMol / 1000000f) * gas.Properties.MolarMass;

				var regionIndex = grid.GetRegionUMoleIndex(g, grid.SentinelRegionIndex);
				grid.RegionGasComposition.uMoles[regionIndex] = molesUMol;
			}

			float averageMolarMass = initialMassCapacity / (environment.TotalUMolesInOneCell / 1000000f);

			grid.RegionPhysicsBuffer.TemperatureK[grid.SentinelRegionIndex]    = environment.TemperatureK;
			grid.RegionPhysicsBuffer.StructuralTemperatureK[grid.SentinelRegionIndex] = environment.TemperatureK;
			grid.RegionPhysicsBuffer.PreviousTemperatureK[grid.SentinelRegionIndex] = environment.TemperatureK;
			grid.RegionPhysicsBuffer.PreviousStructuralTemperatureK[grid.SentinelRegionIndex] = environment.TemperatureK;
			grid.RegionPhysicsBuffer.RegionVolumes[grid.SentinelRegionIndex]   = CellVolume;
			grid.RegionGasComposition.PressureKpa[grid.SentinelRegionIndex]    = environment.TotalPressureKpa;
			grid.RegionGasComposition.TotalUMoles[grid.SentinelRegionIndex]    = environment.TotalUMolesInOneCell;
			grid.RegionPhysicsBuffer.AverageMolarMass[grid.SentinelRegionIndex] = averageMolarMass;
		}

		public static ExternalEnvironment Lerp(ExternalEnvironment a, ExternalEnvironment b, float t)
		{
			t = Math.Max(0, Math.Min(1, t));
			var result = new ExternalEnvironment
			{
				Name = t < 0.5f ? a.Name : b.Name,
				TemperatureK = a.TemperatureK + (b.TemperatureK - a.TemperatureK) * t,
				TotalPressureKpa = a.TotalPressureKpa + (b.TotalPressureKpa - a.TotalPressureKpa) * t,
				GasProportions = new List<GasProportion>()
			};

			var merged = new Dictionary<string, float>();

			if (a.GasProportions != null)
			{
				foreach (var gp in a.GasProportions)
				{
					if (!string.IsNullOrEmpty(gp.Formula))
						merged[gp.Formula!] = gp.Proportion * (1f - t);
				}
			}

			if (b.GasProportions != null)
			{
				foreach (var gp in b.GasProportions)
				{
					if (string.IsNullOrEmpty(gp.Formula)) continue;
					if (merged.ContainsKey(gp.Formula!))
						merged[gp.Formula!] += gp.Proportion * t;
					else
						merged[gp.Formula!] = gp.Proportion * t;
				}
			}

			foreach (var kvp in merged)
			{
				if (kvp.Value > 0f)
				{
					result.GasProportions.Add(new GasProportion(kvp.Key) { Proportion = kvp.Value });
				}
			}

			return result;
		}

		/// <summary>
		/// Returns a new environment whose temperature, pressure, and gas proportions
		/// are the arithmetic mean of all supplied environments. Useful for blending
		/// multiple simultaneous atmospheric incidents into a single ambient target.
		/// Gas proportions are averaged per-formula; since each input environment's
		/// proportions sum to 1, the result's proportions also sum to 1.
		/// </summary>
		public static ExternalEnvironment Average(IEnumerable<ExternalEnvironment> environments)
		{
			float tempK       = 0f;
			float pressureKpa = 0f;
			int   count       = 0;
			var   proportions = new Dictionary<string, float>();
			var   names       = new List<string>();

			foreach (var env in environments)
			{
				tempK       += env.TemperatureK;
				pressureKpa += env.TotalPressureKpa;
				count++;

				if (!string.IsNullOrEmpty(env.Name)) names.Add(env.Name);

				if (env.GasProportions == null) continue;
				foreach (var gp in env.GasProportions)
				{
					if (string.IsNullOrEmpty(gp.Formula)) continue;
					proportions.TryGetValue(gp.Formula!, out float existing);
					proportions[gp.Formula!] = existing + gp.Proportion;
				}
			}

			if (count == 0) throw new ArgumentException("Cannot average an empty collection of environments.");

			float inv = 1f / count;
			var result = new ExternalEnvironment
			{
				Name             = string.Join("+", names),
				TemperatureK     = tempK       * inv,
				TotalPressureKpa = pressureKpa * inv,
				GasProportions   = new List<GasProportion>(),
			};

			foreach (var kvp in proportions)
			{
				float avg = kvp.Value * inv;
				if (avg > 0f)
					result.GasProportions.Add(new GasProportion(kvp.Key) { Proportion = avg });
			}

			return result;
		}

		public void Dispose()
		{
			if (GasProportionsById.IsCreated) GasProportionsById.Dispose();
			if (uMolesInOneCell.IsCreated) uMolesInOneCell.Dispose();
		}
	}
}