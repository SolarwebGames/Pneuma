using System;
using SolarWeb.Pneuma.Grid;

namespace SolarWeb.Pneuma.Data
{
	public struct CellState
	{
		public int WorldPosition;
		public long[] uMoles;
		public float[] uMolesResidue;

		public long[] SolidUMoles;
		public long[] LiquidUMoles;
		public float TemperatureK;
		public bool IsBurning;

		public bool HasValidPriorState;

		public static CellState CaptureState(int worldIdx, AtmosphereGrid grid)
		{
			int regIdx = grid.GridLookups.WorldToRegionIndex[worldIdx];
			if (regIdx < 0 || regIdx == grid.SentinelRegionIndex) return default;

			float regionVol = grid.RegionPhysicsBuffer.RegionVolumes[regIdx];
			float regionCellCount = regionVol > 0f ? regionVol / AtmosphereGrid.DefaultCellVolume : 1f;
			float invCount = 1f / Math.Max(1f, regionCellCount);

			var state = new CellState
			{
				WorldPosition = worldIdx,
				uMoles = new long[grid.GasCount],
				uMolesResidue = new float[grid.GasCount],
				SolidUMoles = new long[grid.GasCount],
				LiquidUMoles = new long[grid.GasCount],
				TemperatureK = grid.RegionPhysicsBuffer.TemperatureK[regIdx],
				IsBurning = false,
				HasValidPriorState = true
			};

			for (int g = 0; g < grid.GasCount; g++)
			{
				var idx = grid.GetRegionUMoleIndex(g, regIdx);
				state.uMoles[g] = (long)(grid.RegionGasComposition.uMoles[idx] * invCount);
				state.SolidUMoles[g] = (long)(grid.SolidComposition.uMoles[idx] * invCount);
				state.LiquidUMoles[g] = (long)(grid.LiquidComposition.uMoles[idx] * invCount);
			}

			return state;
		}

		public readonly void ApplyToGrid(AtmosphereGrid grid)
		{
			var regIdx = grid.GridLookups.WorldToRegionIndex[WorldPosition];
			if (regIdx == -1 || regIdx == grid.SentinelRegionIndex) return;

			for (int g = 0; g < grid.GasCount; g++)
			{
				var regUMoleIdx = grid.GetRegionUMoleIndex(g, regIdx);
				grid.RegionGasComposition.uMoles[regUMoleIdx] += uMoles[g];
				grid.RegionGasComposition.TotalUMoles[regIdx] += uMoles[g];
				if (SolidUMoles != null) grid.SolidComposition.uMoles[regUMoleIdx] += SolidUMoles[g];
				if (LiquidUMoles != null) grid.LiquidComposition.uMoles[regUMoleIdx] += LiquidUMoles[g];
			}
		}

		/// <summary>
		/// Specialized application for runtime rebuilds. Transfers mass into the new topology
		/// without forcing cells to become active overlays unless they were already active.
		/// </summary>
		public readonly void ApplyAsRegionalSlice(AtmosphereGrid grid)
		{
			var regIdx = grid.GridLookups.WorldToRegionIndex[WorldPosition];
			if (regIdx == -1 || regIdx == grid.SentinelRegionIndex) return;

			for (int g = 0; g < grid.GasCount; g++)
			{
				var regUMoleIdx = grid.GetRegionUMoleIndex(g, regIdx);
				grid.RegionGasComposition.uMoles[regUMoleIdx] += uMoles[g];
				grid.RegionGasComposition.TotalUMoles[regIdx] += uMoles[g];
				if (SolidUMoles != null) grid.SolidComposition.uMoles[regUMoleIdx] += SolidUMoles[g];
				if (LiquidUMoles != null) grid.LiquidComposition.uMoles[regUMoleIdx] += LiquidUMoles[g];
			}
		}
	}
}
