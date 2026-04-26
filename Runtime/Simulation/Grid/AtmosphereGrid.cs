using System;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using SolarWeb.Pneuma.Data;

namespace SolarWeb.Pneuma.Grid
{
  public class AtmosphereGrid : IDisposable
  {
    public const byte MASK_EXPOSED = 0x01;
    public const byte MASK_CLEAR_EXPOSED = 0xFE;
    public const float DefaultCellVolume = 2.5f;

    /// <summary>
    /// Maximum number of simultaneously active dynamic regions (split cells).
    /// Each uses one slot in the region pool at the end of every region buffer.
    /// </summary>
    public const int MaxDynRegions = 3000;

    public ExternalEnvironment AmbientEnvironment = null!;

    public int SimCellCount;
    public int WorldCellCount;
    public int SimRegionCount;         // number of static regions built from the map
    public int DynamicRegionPoolStart; // = SimRegionCount; first dynamic slot index
    public int DynRegionCount;         // number of currently live dynamic regions

    /// <summary>Sentinel cell index (= SimCellCount). Cells mapped here have no sim data.</summary>
    public int SentinelCellIndex => SimCellCount;

    /// <summary>
    /// Sentinel region index. Shifted outward from the old SimRegionCount to leave room
    /// for the dynamic pool. Set by AtmosphereGridBuilder to DynamicRegionPoolStart + MaxDynRegions.
    /// </summary>
    public int SentinelRegionIndex;

    public int GasCount;
    public int MapWidth;
    public int MapHeight;
    public int Stride;
    public int RegionStride;

    // ── Core Grid ──────────────────────────────────────────────────
    public GridLookups GridLookups = new();
    public WorldPhysicsBuffer WorldPhysicsBuffer = new();

    // ── Region Simulation ──────────────────────────────────────────
    public RegionPhysicsBuffer RegionPhysicsBuffer = new();
    public RegionGasComposition RegionGasComposition = new();
    public RegionFaceBuffer RegionFaceBuffer = new();
    public RegionStates RegionStates = new();
    public LiquidComposition LiquidComposition = new();
    public LiquidComposition SolidComposition = new();
    public GasStoichiometry GasStoichiometry = new();
    public PlantMetabolismRegistry PlantMetabolismRegistry = new();
    public RegionPlantPopulation RegionPlantPopulation = new();
    public RegionPlantStates RegionPlantStates = new();
    public TopologyBuffer TopologyBuffer = new();

    // ── Dynamic Simulation ─────────────────────────────────────────
    public DynamicRegionBuffer DynamicRegions = new();
    public DynamicFaceBuffer DynamicFaces = new();

    // ── Event Buffers ──────────────────────────────────────────────
    public AtmosphereEvents Events = new();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetRegionUMoleIndex(int gasIndex, int regionSimIndex) => (gasIndex * RegionStride) + regionSimIndex;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetSimIndex(int worldIdx)
    {
      if (worldIdx < 0 || worldIdx >= GridLookups.WorldToSimIndex.Length) return -2;
      return GridLookups.WorldToSimIndex[worldIdx];
    }

    public void RecalculateRegionPressure(int regionIdx)
    {
      if (regionIdx < 0 || regionIdx >= RegionStride) return;

      float volume = RegionPhysicsBuffer.RegionVolumes[regionIdx];
      if (volume < 0.001f)
      {
        RegionGasComposition.PressureKpa[regionIdx] = 0f;
        return;
      }

      long totalUMoles = 0;
      for (int g = 0; g < GasCount; g++)
      {
        totalUMoles += RegionGasComposition.uMoles[GetRegionUMoleIndex(g, regionIdx)];
      }
      RegionGasComposition.TotalUMoles[regionIdx] = totalUMoles;

      double moles = totalUMoles / 1e6;
      double pressurePa = (moles * SolarWeb.Pneuma.Constants.AtmosphereConstants.R * RegionPhysicsBuffer.TemperatureK[regionIdx]) / volume;
      RegionGasComposition.PressureKpa[regionIdx] = (float)(pressurePa / 1000.0);
    }

    public XElement CaptureAtmosphereSnapshot(int worldIdx, Func<int, string> getGasName)
    {
      var root = new XElement("AtmosphereSnapshot");

      int regIdx = (worldIdx >= 0 && worldIdx < GridLookups.WorldToRegionIndex.Length)
        ? GridLookups.WorldToRegionIndex[worldIdx] : -1;
      if (regIdx < 0) regIdx = SentinelRegionIndex;

      var region = new XElement("Region", new XAttribute("index", regIdx));
      region.Add(new XAttribute("pressure", RegionGasComposition.PressureKpa[regIdx]));
      region.Add(new XAttribute("temperature", RegionPhysicsBuffer.TemperatureK[regIdx]));

      bool isDynamic = regIdx >= DynamicRegionPoolStart && regIdx < SentinelRegionIndex;
      if (isDynamic)
        region.Add(new XAttribute("dynamic", true));

      for (int g = 0; g < GasCount; g++)
      {
        long amt = RegionGasComposition.uMoles[g * RegionStride + regIdx];
        if (amt > 0)
        {
          region.Add(new XElement("Gas",
            new XAttribute("name", getGasName(g)),
            new XAttribute("uMol", amt)));
        }
      }
      root.Add(region);

      return root;
    }

    public void Dispose()
    {
      GridLookups.Dispose();
      WorldPhysicsBuffer.Dispose();
      TopologyBuffer.Dispose();
      RegionPhysicsBuffer.Dispose();
      RegionGasComposition.Dispose();
      RegionFaceBuffer.Dispose();
      RegionStates.Dispose();
      LiquidComposition.Dispose();
      SolidComposition.Dispose();
      GasStoichiometry.Dispose();
      PlantMetabolismRegistry.Dispose();
      RegionPlantPopulation.Dispose();
      RegionPlantStates.Dispose();

      DynamicRegions.Dispose();
      DynamicFaces.Dispose();
      Events.Dispose();
    }
  }
}