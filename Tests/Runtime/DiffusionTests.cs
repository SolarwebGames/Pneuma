using SolarWeb.Pneuma.Simulation;
using SolarWeb.Pneuma.Grid;
using NUnit.Framework;

namespace SolarWeb.Pneuma.Tests
{
  [TestFixture]
  public class DiffusionTests
  {
    [Test]
    public void GasDiffusion_BetweenTwoRegions_MovesGasesCorrectly()
    {
      AtmosphereGrid grid;
      using (var manager = TestSimulationHelper.CreateTwoRegionSim(out grid))
      {
        int n2Idx = 0;
        int reg0 = 0;
        int reg1 = 1;

        // Clear initial ambient gases to start with a vacuum in region 1
        for (int g = 0; g < grid.GasCount; g++)
        {
          grid.RegionGasComposition.uMoles[grid.GetRegionUMoleIndex(g, reg0)] = 0;
          grid.RegionGasComposition.uMoles[grid.GetRegionUMoleIndex(g, reg1)] = 0;
        }

        // Add 100 moles of N2 to region 0
        grid.RegionGasComposition.uMoles[grid.GetRegionUMoleIndex(n2Idx, reg0)] = 100_000_000;

        // Manually sync totals/pressure before first tick
        grid.RecalculateRegionPressure(reg0);
        grid.RecalculateRegionPressure(reg1);

        // Run a single simulation tick
        manager.Tick(1.0f);

        // Verify gas has moved from region 0 to region 1
        long reg0_N2 = grid.RegionGasComposition.uMoles[grid.GetRegionUMoleIndex(n2Idx, reg0)];
        long reg1_N2 = grid.RegionGasComposition.uMoles[grid.GetRegionUMoleIndex(n2Idx, reg1)];

        Assert.Less(reg0_N2, 100_000_000, $"Region 0 should have lost N2. Current: {reg0_N2}");
        Assert.Greater(reg1_N2, 0, $"Region 1 should have gained N2. Current: {reg1_N2}");
        Assert.AreEqual(100_000_000, reg0_N2 + reg1_N2);
      }
    }
  }
}
