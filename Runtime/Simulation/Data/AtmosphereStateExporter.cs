using System;
using System.IO;
using System.Text;
using SolarWeb.Pneuma.Grid;

namespace SolarWeb.Pneuma.Simulation.Data
{
  [Flags]
  public enum ExportFlags : int
  {
    None = 0,
    GasComposition = 1 << 0,
    Thermal = 1 << 1,
    Topology = 1 << 2,
    Metabolism = 1 << 3,
    State = 1 << 4,
    All = ~0
  }

  /// <summary>
  /// Handles snapshots of the entire grid state for external analysis or serialization.
  /// </summary>
  public class AtmosphereStateExporter
  {
    private readonly AtmosphereManager manager;
    private AtmosphereGrid State => manager.State;

    public AtmosphereStateExporter(AtmosphereManager manager)
    {
      this.manager = manager;
    }

    /// <summary>
    /// Exports the current grid state to an XML file based on provided flags.
    /// </summary>
    public void ExportGridStateXml(string filePath, ExportFlags flags, int targetRegionIndex = -1)
    {
      var sb = new StringBuilder();
      sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
      sb.AppendLine("<AtmosphereState>");

      if ((flags & ExportFlags.GasComposition) != 0)
      {
        ExportGasComposition(sb, targetRegionIndex);
      }

      if ((flags & ExportFlags.Thermal) != 0)
      {
        ExportThermalData(sb, targetRegionIndex);
      }

      if ((flags & ExportFlags.State) != 0)
      {
        ExportRegionStateData(sb, targetRegionIndex);
      }

      sb.AppendLine("</AtmosphereState>");
      File.WriteAllText(filePath, sb.ToString());
    }

    private void ExportGasComposition(StringBuilder sb, int targetRegionIndex)
    {
      sb.AppendLine("  <GasComposition>");
      int start = targetRegionIndex >= 0 ? targetRegionIndex : 0;
      int end = targetRegionIndex >= 0 ? targetRegionIndex + 1 : State.RegionStride;

      for (int r = start; r < end; r++)
      {
        if (r == State.SentinelRegionIndex || (targetRegionIndex == -1 && State.RegionGasComposition.TotalUMoles[r] <= 0))
          continue;

        sb.Append($"    <Region id=\"{r}\" totalUMoles=\"{State.RegionGasComposition.TotalUMoles[r]}\">");
        for (int g = 0; g < manager.GasRegistry.GasCount; g++)
        {
          long umol = State.RegionGasComposition.uMoles[g * State.RegionStride + r];
          if (umol > 0)
          {
            var gas = manager.GasRegistry.AllGases[g];
            sb.Append($" <Gas id=\"{g}\" name=\"{gas.Name}\" umol=\"{umol}\" />");
          }
        }
        sb.AppendLine("</Region>");
      }
      sb.AppendLine("  </GasComposition>");
    }

    private void ExportThermalData(StringBuilder sb, int targetRegionIndex)
    {
      sb.AppendLine("  <ThermalData>");
      int start = targetRegionIndex >= 0 ? targetRegionIndex : 0;
      int end = targetRegionIndex >= 0 ? targetRegionIndex + 1 : State.RegionStride;

      for (int r = start; r < end; r++)
      {
        if (r == State.SentinelRegionIndex) continue;

        sb.AppendLine($"    <Region id=\"{r}\" tempK=\"{State.RegionPhysicsBuffer.TemperatureK[r]:F2}\" enclosingTempK=\"{State.RegionPhysicsBuffer.EnclosingTemperatureK[r]:F2}\" internalMassTempK=\"{State.RegionPhysicsBuffer.InternalMassTemperatureK[r]:F2}\"");
      }
      sb.AppendLine("  </ThermalData>");
    }

    private void ExportRegionStateData(StringBuilder sb, int targetRegionIndex)
    {
      sb.AppendLine("  <RegionStates>");
      int start = targetRegionIndex >= 0 ? targetRegionIndex : 0;
      int end = targetRegionIndex >= 0 ? targetRegionIndex + 1 : State.RegionStride;

      for (int r = start; r < end; r++)
      {
        if (r == State.SentinelRegionIndex) continue;

        sb.AppendLine($"    <Region id=\"{r}\" pressureKpa=\"{State.RegionGasComposition.PressureKpa[r]:F2}\" isBurning=\"{State.RegionStates.IsBurning[r]}\" burnIntensity=\"{State.RegionStates.BurnIntensity[r]:F4}\" />");
      }
      sb.AppendLine("  </RegionStates>");
    }
  }
}
