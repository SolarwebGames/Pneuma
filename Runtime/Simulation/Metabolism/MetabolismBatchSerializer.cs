using System;
using System.Xml.Linq;

namespace SolarWeb.Pneuma.Metabolism
{
  public static class MetabolismBatchSerializer
  {
    public static XElement CaptureSnapshot(MetabolismBatch batch, Func<int, string> getGasName)
    {
      var root = new XElement("SimulationSnapshot",
        new XAttribute("metabolismId", batch.MetabolismId),
        new XAttribute("template", batch.Template.Name),
        new XAttribute("gasCount", batch.GasCount),
        new XAttribute("globinCount", batch.GlobinCount),
        new XAttribute("invPlasmaCapacity", batch.InvPlasmaCapacity.ToString("R"))
      );

      var props = new XElement("Properties",
        new XAttribute("ventilationRate", batch.Settings.VentilationRate),
        new XAttribute("lungCapacity", batch.Settings.LungCapacityUMol),
        new XAttribute("plasmaCapacity", batch.Settings.PlasmaCapacityUMol),
        new XAttribute("plasmaExchangeRate", batch.Settings.PlasmaExchangeRate),
        new XAttribute("plasmaFiltrationRate", batch.Settings.PlasmaFiltrationRateUMol),
        new XAttribute("allostericSensitivity", batch.Settings.AllostericSensitivity.ToString("R"))
      );
      root.Add(props);

      var profileElem = new XElement("AllostericProfile",
        new XAttribute("dipoleSensitivity", batch.Template.AllostericProfile.DipoleSensitivity.ToString("R")),
        new XAttribute("enThreshold", batch.Template.AllostericProfile.ElectronegativityThreshold.ToString("R")),
        new XAttribute("exemptions", string.Join(",", batch.Template.AllostericProfile.ExemptGases))
      );
      root.Add(profileElem);

      var globins = new XElement("Globins");
      for (int i = 0; i < batch.GlobinCount; i++)
      {
        var spec = batch.GlobinSpecs[i];
        globins.Add(new XElement("Globin",
          new XAttribute("index", i),
          new XAttribute("name", spec.Name),
          new XAttribute("capacity", batch.GlobinCapacities[i]),
          new XAttribute("hillN", spec.HillCoefficient.ToString("R")),
          new XAttribute("p50", spec.P50LungFraction.ToString("R")),
          new XAttribute("baseAffinity", spec.BaseAffinity.ToString("R"))
        ));
      }
      root.Add(globins);

      var globinGases = new XElement("GlobinGases");
      for (int bi = 0; bi < batch.GlobinGasIds.Length; bi++)
      {
        int g = batch.GlobinGasIds[bi];
        globinGases.Add(new XElement("Gas",
          new XAttribute("id", g),
          new XAttribute("name", getGasName(g)),
          new XAttribute("globinIdx", batch.GlobinGasGlobinIds[bi]),
          new XAttribute("bindAffinity", batch.GlobinGasBindAffinities[bi].ToString("R")),
          new XAttribute("hillN", batch.GlobinHillCoeffs[bi].ToString("R")),
          new XAttribute("p50", batch.GlobinP50Fracs[bi].ToString("R")),
          new XAttribute("uptakeFactor", batch.GlobinGasLungUptakeFactors[bi].ToString("R")),
          new XAttribute("releaseFactor", batch.GlobinGasLungReleaseFactors[bi].ToString("R")),
          new XAttribute("swp", batch.GasStructuralWarpingPotentials[g].ToString("R"))
        ));
      }
      root.Add(globinGases);

      var freeGases = new XElement("FreeGases");
      for (int fi = 0; fi < batch.FreeGasIds.Length; fi++)
      {
        int g = batch.FreeGasIds[fi];
        freeGases.Add(new XElement("Gas",
          new XAttribute("id", g),
          new XAttribute("name", getGasName(g)),
          new XAttribute("uptakeFactor", batch.FreeGasUptakeFactors[fi].ToString("R")),
          new XAttribute("releaseFactor", batch.FreeGasReleaseFactors[fi].ToString("R")),
          new XAttribute("swp", batch.GasStructuralWarpingPotentials[g].ToString("R"))
        ));
      }
      root.Add(freeGases);

      var reactions = new XElement("Reactions");
      for (int r = 0; r < batch.Reactions.Length; r++)
      {
        var rx = batch.Reactions[r];
        reactions.Add(new XElement("Reaction",
          new XAttribute("index", r),
          new XAttribute("input", getGasName(rx.InputId)),
          new XAttribute("output", rx.OutputId >= 0 ? getGasName(rx.OutputId) : "None"),
          new XAttribute("globin", rx.TargetGlobinId >= 0 ? rx.TargetGlobinId.ToString() : "None"),
          new XAttribute("rate", rx.TargetMetabolicRatePerSecond),
          new XAttribute("efficiency", rx.EfficiencyBP)
        ));
      }
      root.Add(reactions);

      var subs = new XElement("Subscriptions");
      for (int s = 0; s < batch.SubscriptionCount; s++)
      {
        subs.Add(new XElement("Subscription",
          new XAttribute("index", s),
          new XAttribute("gas", getGasName(batch.SubGasIds[s])),
          new XAttribute("threshold", batch.SubThresholds[s].ToString("R")),
          new XAttribute("hysteresis", batch.SubHysteresis[s].ToString("R")),
          new XAttribute("isExcess", batch.SubIsExcess[s]),
          new XAttribute("isPlasma", batch.SubIsPlasma[s])
        ));
      }
      root.Add(subs);

      return root;
    }
  }
}
