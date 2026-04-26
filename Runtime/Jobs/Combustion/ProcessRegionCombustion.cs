using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

using SolarWeb.Pneuma.MathA;
using SolarWeb.Pneuma.Logging;

namespace SolarWeb.Pneuma.Jobs.Combustion
{
	[BurstCompile(OptimizeFor = OptimizeFor.Performance)]
	public struct ProcessRegionCombustion : IJobParallelForDefer
	{
		[ReadOnly] public NativeArray<int> ActiveRegionIndices;
		[NativeDisableParallelForRestriction] public NativeArray<long> uMoles;
		[ReadOnly] public NativeArray<long> TotalUMoles;
		/// <summary>Merged total (residual + overlay cells) used for concentration / fraction
		/// checks so that overlay gas correctly dilutes or concentrates fuel percentages.
		/// Heat-capacity calculations still use the raw TotalUMoles so that the region
		/// temperature change reflects only the region's own thermal mass.</summary>
		[ReadOnly] public NativeArray<long> EffectiveTotalUMoles;
		public NativeArray<float> TemperatureK;
		[ReadOnly] public NativeArray<bool> IsBurning;
		public NativeArray<float> BurnIntensity;

		[ReadOnly] public NativeArray<float> AutoIgnitionTemperature;
		[ReadOnly] public NativeArray<float> OxidizingPotency;
		[ReadOnly] public NativeArray<float> LowerExplosiveLimit;
		[ReadOnly] public NativeArray<float> UpperExplosiveLimit;
		[ReadOnly] public NativeArray<float> EnthalpyOfCombustion_Jmol;

		[ReadOnly] public NativeArray<int> AtomicCompositionIndices;
		[ReadOnly] public NativeArray<int> CompositionOffsets;
		[ReadOnly] public NativeArray<int> ElementOxideGasId;
		[ReadOnly] public NativeArray<float> ElementOxideStoichiometry;

		[ReadOnly] public NativeArray<float> MixtureMolarCp;
		[ReadOnly] public NativeArray<float> PressureKpa;

		public int GasCount;
		public int RegionStride;
		public float TimeStep;
		public int SentinelIndex;
		public float MinCombustionPressureKpa;

		public void Execute(int index)
		{
			int rIdx = ActiveRegionIndices[index];
			BurnIntensity[rIdx] = 0;

			if (rIdx == SentinelIndex || !IsBurning[rIdx]) return;

			long effectiveUMol = EffectiveTotalUMoles[rIdx];
			if (effectiveUMol <= 1000) return;

			if (PressureKpa[rIdx] < MinCombustionPressureKpa) return;

			long totalUMol = TotalUMoles[rIdx];

			double invTotal = 1.0 / (double)effectiveUMol;

			double totalOxidizerPotentialUMol = 0;
			for (int g = 0; g < GasCount; g++)
			{
				float potency = OxidizingPotency[g];
				if (potency > 0)
				{
					totalOxidizerPotentialUMol += (double)uMoles[g * RegionStride + rIdx] * (double)potency;
				}
			}

			if (totalOxidizerPotentialUMol <= 100) return;

			double totalHeatGenerated_J = 0;
			double totalPotentialConsumed_uMol = 0;
			double totalFuelBurned_uMol = 0;

			for (int g = 0; g < GasCount; g++)
			{
				float ait = AutoIgnitionTemperature[g];
				if (ait <= 0f || OxidizingPotency[g] > 0.5f) continue;

				long fuelUMol = uMoles[g * RegionStride + rIdx];
				if (fuelUMol <= 0) continue;

				double fuelPercentage = ((double)fuelUMol * invTotal) * 100.0;

				if (fuelPercentage >= (double)LowerExplosiveLimit[g] && fuelPercentage <= (double)UpperExplosiveLimit[g])
				{
					double maxBurn_uMol = (double)fuelUMol * 0.10 * (double)TimeStep;

					int start = CompositionOffsets[g];
					int end = CompositionOffsets[g + 1];
					int oxidizableAtoms = 0;
					for (int i = start; i < end; i++)
					{
						int atomIdx = AtomicCompositionIndices[i];
						if (atomIdx >= 0 && atomIdx < ElementOxideGasId.Length && ElementOxideGasId[atomIdx] != -1)
						{
							oxidizableAtoms++;
						}
					}

					double neededPotential_uMol = maxBurn_uMol * math.max(1, oxidizableAtoms);
					double actualBurn_uMol = maxBurn_uMol;

					if (neededPotential_uMol > (totalOxidizerPotentialUMol - totalPotentialConsumed_uMol))
					{
						actualBurn_uMol = (totalOxidizerPotentialUMol - totalPotentialConsumed_uMol) / math.max(1, oxidizableAtoms);
					}

					if (actualBurn_uMol > 1.0)
					{
						uMoles[g * RegionStride + rIdx] -= (long)actualBurn_uMol;
						totalPotentialConsumed_uMol += actualBurn_uMol * oxidizableAtoms;
						totalFuelBurned_uMol += actualBurn_uMol;

						for (int i = start; i < end; i++)
						{
							int atomIdx = AtomicCompositionIndices[i];
							if (atomIdx >= 0 && atomIdx < ElementOxideGasId.Length)
							{
								int productGasId = ElementOxideGasId[atomIdx];
								if (productGasId >= 0 && productGasId < GasCount)
								{
									float stoichiometry = ElementOxideStoichiometry[atomIdx];
									if (stoichiometry > 0)
									{
										uMoles[productGasId * RegionStride + rIdx] += (long)(actualBurn_uMol / (double)stoichiometry);
									}
								}
							}
						}

						double burnedMoles = actualBurn_uMol * 1e-6;
						totalHeatGenerated_J += burnedMoles * (double)EnthalpyOfCombustion_Jmol[g];
					}
				}
			}

			if (totalPotentialConsumed_uMol > 0 && totalOxidizerPotentialUMol > 0)
			{
				double consumeRatio = totalPotentialConsumed_uMol / totalOxidizerPotentialUMol;
				for (int g = 0; g < GasCount; g++)
				{
					float potency = OxidizingPotency[g];
					if (potency > 0)
					{
						int dataIdx = g * RegionStride + rIdx;
						long consumed = (long)((double)uMoles[dataIdx] * consumeRatio);
						uMoles[dataIdx] -= consumed;
					}
				}
			}

			if (totalFuelBurned_uMol > 0)
			{
				if (totalHeatGenerated_J != 0)
				{
					float airCapacity = MixtureMolarCp[rIdx] * (float)(totalUMol * 1e-6);
					float capacity = airCapacity * AtmosphereCalc.GetTemperatureCapacityFactor(TemperatureK[rIdx]);

					if (capacity > 1e-6f)
					{
						TemperatureK[rIdx] += (float)(totalHeatGenerated_J / capacity);
					}
				}

				float finalIntensity = (float)(totalFuelBurned_uMol / (double)TimeStep);
				BurnIntensity[rIdx] = finalIntensity;
			}
		}
	}
}
