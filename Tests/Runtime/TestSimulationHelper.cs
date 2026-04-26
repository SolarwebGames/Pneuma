using System.Collections.Generic;
using SolarWeb.Pneuma.Atoms;
using SolarWeb.Pneuma.Data;
using SolarWeb.Pneuma.Gas;
using SolarWeb.Pneuma.Grid;
using SolarWeb.Pneuma.Simulation;
using Unity.Collections;
using Unity.Mathematics;

namespace SolarWeb.Pneuma.Tests
{
  public static class TestSimulationHelper
  {
    public static AtmosphereManager CreateTwoRegionSim(out AtmosphereGrid grid, float tempK = 293.15f, float pressureKpa = 101.325f)
    {
      var atoms = new List<AtomDefinition>
            {
                new AtomDefinition { Name = "Oxygen", Symbol = "O", AtomicNumber = 8, Properties = new AtomicProperties { AtomicMass_u = 15.999f } },
                new AtomDefinition { Name = "Nitrogen", Symbol = "N", AtomicNumber = 7, Properties = new AtomicProperties { AtomicMass_u = 14.007f } }
            };
      var gases = new List<GasDefinition>
            {
                new GasDefinition
                {
                    Id = 0,
                    Name = "N2",
                    ChemicalFormula = "N2",
                    Properties = new GasProperties
                    {
                        MolarMass = 28.014f,
                        CollisionDiameterAngstroms = 3.64f,
                        DiffusionConstant = 1.0f,
                        MolarHeatCapacityCp = 29.1f,
                        MolarHeatCapacityCv = 20.8f
                    }
                },
                new GasDefinition
                {
                    Id = 1,
                    Name = "O2",
                    ChemicalFormula = "O2",
                    Properties = new GasProperties
                    {
                        MolarMass = 31.999f,
                        CollisionDiameterAngstroms = 3.46f,
                        DiffusionConstant = 1.0f,
                        MolarHeatCapacityCp = 29.4f,
                        MolarHeatCapacityCv = 21.1f
                    }
                }
            };

      var config = new SimulationConfig
      {
        MapWidth = 2,
        MapHeight = 1,
        Gases = gases.ToArray(),
        Ambient = new ExternalEnvironment
        {
          TemperatureK = tempK,
          TotalPressureKpa = pressureKpa,
          GasProportions = new List<ExternalEnvironment.GasProportion>
                    {
                        new ExternalEnvironment.GasProportion("N2") { Proportion = 0.79f },
                        new ExternalEnvironment.GasProportion("O2") { Proportion = 0.21f }
                    }
        },
        CellData = new List<CellInitializationData>
                {
                    new CellInitializationData { WorldIndex = 0, WorldPos = new WorldPos(0, 0), RegionIndex = 0, CellProperties = StructuralProperties.Empty },
                    new CellInitializationData { WorldIndex = 1, WorldPos = new WorldPos(1, 0), RegionIndex = 1, CellProperties = StructuralProperties.Empty }
                },
        RegionData = new List<RegionInitializationData>
                {
                    new RegionInitializationData { SimIndex = 0, RoomID = 1, TemperatureK = tempK, RegionCells = new List<int> { 0 } },
                    new RegionInitializationData { SimIndex = 1, RoomID = 1, TemperatureK = tempK, RegionCells = new List<int> { 1 } }
                },
        RegionFaces = new List<RegionFaceLink>
                {
                    new RegionFaceLink { RegionA = 0, RegionB = 1, FaceType = 1, TotalPermeability = 1.0f, TotalSurfaceArea = 1.0f }
                },
        RegionFaceCellPairOffsets = new List<int> { 0 },
        RegionFaceCellPairCounts = new List<int> { 1 },
        RegionFaceCellSimA = new List<int> { 0 },
        RegionFaceCellSimB = new List<int> { 1 }
      };

      config.CellThermalConductivity = new float[2] { 0.1f, 0.1f };
      config.CellThermalCapacity = new float[2] { 1000f, 1000f };
      config.CellStructuralConductance = new float[2] { 0.1f, 0.1f };
      config.CellGasPermeability = new float[2] { 1.0f, 1.0f };
      config.CellFlowArea = new float[2] { 1.0f, 1.0f };
      config.CellMaxPressureDeltaKpa = new float[2] { 1000f, 1000f };
      config.CellMinColDia = new float[2];
      config.CellMaxColDia = new float[2] { 10f, 10f };
      config.CellPumpRate = new float[2];
      config.CellFlowDirX = new sbyte[2];
      config.CellFlowDirZ = new sbyte[2];
      config.CellFlags = new byte[2] { 0x08, 0x08 }; // bit3 = isSimulated
      config.LinkOffsets = new int[0];
      config.LinkCounts = new int[0];
      config.LinkStartWorldIndices = new int[0];
      config.LinkDirections = new int3[0];

      var gasRegistry = new GasRegistry();
      var atomicRegistry = new AtomicRegistry();
      atomicRegistry.Initialize(atoms);
      var stoichiometry = new GasStoichiometry();
      stoichiometry.Initialize(gases.Count, atomicRegistry.TotalAtomCount);
      gasRegistry.Initialize(gases, atomicRegistry, stoichiometry);

      grid = AtmosphereGridBuilder.RebuildSimulation(config, gasRegistry);
      var manager = new AtmosphereManager(grid);
      manager.Initialize(atoms, gases);

      return manager;
    }
  }
}
