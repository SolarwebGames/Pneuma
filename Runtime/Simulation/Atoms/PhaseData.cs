using System.Collections.Generic;

namespace SolarWeb.Pneuma.Atoms
{
  /// <summary>
  /// A single sampled point on a phase boundary curve.
  /// </summary>
  public struct PhaseBoundaryPoint
  {
    public float P_Bar;
    public float T_K;
  }

  /// <summary>
  /// Antoine equation coefficients for the liquid-gas boundary.
  /// Formula: log10(P_bar) = A - B / (C + T_K)
  /// Valid between the triple point and critical point.
  /// When absent, code falls back to Clausius-Clapeyron using
  /// BoilingPoint_K and EnthalpyVaporization_kJmol.
  /// </summary>
  public class AntoineCoefficients
  {
    public float A;
    public float B;
    public float C;
    public float ValidRangeMin_K;
    public float ValidRangeMax_K;
  }

  /// <summary>
  /// Full pressure-dependent phase boundary data for an element.
  /// Optional — when null, the simulation falls back to Clausius-Clapeyron
  /// using the reference MeltingPoint_K, BoilingPoint_K, and
  /// EnthalpyVaporization_kJmol stored in AtomicProperties.
  /// </summary>
  public class PhaseData
  {
    /// <summary>
    /// Triple point: lower terminus of both phase boundary curves.
    /// The melting curve and vaporization curve meet here.
    /// </summary>
    public PhaseBoundaryPoint TriplePoint;

    /// <summary>
    /// Antoine equation for the liquid-gas boundary, from the triple point
    /// to the critical point. Falls back to Clausius-Clapeyron if null.
    /// </summary>
    public AntoineCoefficients? VaporizationCurve;

    /// <summary>
    /// Sampled points along the solid-liquid boundary, sorted by ascending pressure.
    /// Interpolated linearly at runtime; extrapolates linearly beyond the last point.
    /// A negative dT/dP slope indicates an anomalous element (e.g. water, bismuth).
    /// </summary>
    public List<PhaseBoundaryPoint>? MeltingCurve;
  }
}
