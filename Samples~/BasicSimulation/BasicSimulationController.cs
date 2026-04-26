using UnityEngine;
using SolarWeb.Pneuma.Simulation;
using SolarWeb.Pneuma.Grid;
using SolarWeb.Pneuma.Data;
using SolarWeb.Pneuma.Atoms;
using SolarWeb.Pneuma.Gas;
using System.Collections.Generic;
using System.Linq;

namespace SolarWeb.Pneuma.Samples
{
  /// <summary>
  /// A basic example showing how to initialize and tick the Pneuma atmosphere simulation in Unity.
  /// </summary>
  public class BasicSimulationController : MonoBehaviour
  {
    public int mapWidth = 64;
    public int mapHeight = 64;

    [Header("Data")]
    public List<TextAsset> atomXmlFiles;
    public List<TextAsset> gasXmlFiles;

    private AtmosphereManager manager;
    private bool isInitialized;

    // In a real project, these would likely be loaded from ScriptableObjects or Defs.
    private List<AtomDefinition> atoms = new List<AtomDefinition>();
    private List<GasDefinition> gases = new List<GasDefinition>();

    void Start()
    {
      InitializeSimulation();
    }

    void InitializeSimulation()
    {
      if (atomXmlFiles != null && atomXmlFiles.Count > 0)
      {
        var serializer = new System.Xml.Serialization.XmlSerializer(typeof(List<AtomDefinition>), new System.Xml.Serialization.XmlRootAttribute("Atoms"));
        foreach (var xmlAsset in atomXmlFiles)
        {
          using (var reader = new System.IO.StringReader(xmlAsset.text))
          {
            var loadedAtoms = (List<AtomDefinition>)serializer.Deserialize(reader);
            atoms.AddRange(loadedAtoms);
          }
        }
      }

      if (gasXmlFiles != null && gasXmlFiles.Count > 0)
      {
        var serializer = new System.Xml.Serialization.XmlSerializer(typeof(List<GasDefinition>), new System.Xml.Serialization.XmlRootAttribute("Gases"));
        foreach (var xmlAsset in gasXmlFiles)
        {
          using (var reader = new System.IO.StringReader(xmlAsset.text))
          {
            var loadedGases = (List<GasDefinition>)serializer.Deserialize(reader);
            gases.AddRange(loadedGases);
          }
        }
      }

      // Apply fallback definitions if no external XML sources are provided.
      if (atoms.Count == 0)
      {
        atoms.Add(new AtomDefinition { Name = "Oxygen", Symbol = "O", AtomicNumber = 8 });
        atoms.Add(new AtomDefinition { Name = "Nitrogen", Symbol = "N", AtomicNumber = 7 });
      }
      if (gases.Count == 0)
      {
        gases.Add(new GasDefinition { Id = 0, Name = "N2", ChemicalFormula = "N2" });
        gases.Add(new GasDefinition { Id = 1, Name = "O2", ChemicalFormula = "O2" });
      }

      SimulationConfig config = new SimulationConfig
      {
        MapWidth = mapWidth,
        MapHeight = mapHeight,
        Gases = gases.ToArray(),
        Ambient = new ExternalEnvironment
        {
          TemperatureK = 293.15f,
          TotalPressureKpa = 101.325f
        },
        CellData = new List<CellInitializationData>(),
        RegionData = new List<RegionInitializationData>(),
        RegionFaces = new List<RegionFaceLink>()
      };

      // Initialize with a single region covering the entire grid.
      var allCells = Enumerable.Range(0, mapWidth * mapHeight).ToList();
      config.RegionData.Add(new RegionInitializationData
      {
        RegionCells = allCells,
        TemperatureK = 293.15f,
        RoomID = 1
      });

      AtmosphereGrid grid = AtmosphereGridBuilder.RebuildSimulation(config, new GasRegistry());
      manager = new AtmosphereManager(grid);
      manager.Initialize(atoms, gases);

      isInitialized = true;
      Debug.Log("Pneuma Simulation Initialized.");
    }

    void Update()
    {
      if (!isInitialized) return;

      // Fixed tick intervals (e.g., 10Hz) are generally preferred for stability in production.
      float timeStep = Time.deltaTime;
      manager.Tick(timeStep, 1);
    }

    void OnDestroy()
    {
      // Must dispose the manager to free unmanaged NativeArrays.
      manager?.Dispose();
    }
  }
}
