using System.Collections.Generic;
using SolarWeb.Pneuma.Gas;

namespace SolarWeb.Pneuma.Data
{
	public struct SimulationConfig
	{
		public int MapWidth;
		public int MapHeight;
		public int PlantProfileCount;
		public int TotalAtomCount;
		public GasDefinition[] Gases;
		public ExternalEnvironment Ambient;

		public List<CellInitializationData> CellData;
		public List<FaceLink> CellFaces;

		public List<RegionInitializationData> RegionData;
		public List<RegionFaceLink> RegionFaces;

		// ── Topology & SoA Physics ──────────────────────────────────────────
		public float[] CellThermalConductivity;
		public float[] CellThermalCapacity;
		public float[] CellStructuralConductance;
		public float[] CellGasPermeability;
		public float[] CellFlowArea;
		public float[] CellMaxPressureDeltaKpa;
		public float[] CellMinColDia;
		public float[] CellMaxColDia;
		public float[] CellPumpRate;
		public sbyte[] CellFlowDirX;
		public sbyte[] CellFlowDirZ;
		public byte[] CellFlags;

		public float[] CellTopPermeability;
		public float[] CellTopConductivity;
		public float[] CellBottomPermeability;
		public float[] CellBottomConductivity;

		public IReadOnlyList<int> LinkOffsets;
		public IReadOnlyList<int> LinkCounts;
		public IReadOnlyList<int> LinkStartWorldIndices;
		public IReadOnlyList<Unity.Mathematics.int3> LinkDirections;
		// ────────────────────────────────────────────────────────────────────

		/// <summary>
		/// Per-region-face offset into <see cref="RegionFaceCellSimA"/> / <see cref="RegionFaceCellSimB"/>.
		/// Parallel to <see cref="RegionFaces"/>. Populated by TopologyMapper.AggregateRegionFaces.
		/// </summary>
		public List<int> RegionFaceCellPairOffsets;

		/// <summary>Count of constituent sim-cell pairs for each region face.</summary>
		public List<int> RegionFaceCellPairCounts;

		/// <summary>
		/// Flat list of sim-cell A indices for each region-face constituent pair.
		/// Indexed via RegionFaceCellPairOffsets + RegionFaceCellPairCounts.
		/// </summary>
		public List<int> RegionFaceCellSimA;

		/// <summary>Flat list of sim-cell B indices (parallel to RegionFaceCellSimA).</summary>
		public List<int> RegionFaceCellSimB;

		public List<RegionSaveState> SavedRegions;

		public readonly int CellCount => CellData.Count;
		public readonly int RegionCount => RegionData.Count;
	}
}