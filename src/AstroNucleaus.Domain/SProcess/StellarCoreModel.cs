namespace AstroNucleaus.Domain.SProcess;

/// <summary>
/// Simple prescriptions for the convective He-burning core used by the one-zone s-process model.
/// </summary>
/// <remarks>
/// Density and duration are rough power-law scalings of the kind found in solar-metallicity massive-star
/// models (e.g. Woosley, Heger &amp; Weaver 2002, Rev. Mod. Phys. 74, 1015): about 1500 g/cm³ and 1.9 Myr at
/// 15 M☉, decreasing with mass. They are not a fit to a specific model grid; pass values from your own
/// stellar model through <see cref="SProcessParameters.DensityOverride"/> and
/// <see cref="SProcessParameters.DurationYearsOverride"/> for quantitative work.
///
/// The initial composition is scaled from the solar photosphere of Asplund et al. 2009 (ARA&amp;A 47, 481;
/// Z☉ = 0.0134): all CNO is assumed to be in ²²Ne at the start of core He burning, X(²²Ne) ≈ 1.0·Z;
/// X(Fe) ≈ 0.097·Z with 91.75 % ⁵⁶Fe; X(Ne) ≈ 0.095·Z (as ²⁰Ne); X(Mg) ≈ 0.053·Z
/// split 78.99 / 10.00 / 11.01 % into ²⁴Mg / ²⁵Mg / ²⁶Mg.
/// </remarks>
public static class StellarCoreModel
{
    public const double SecondsPerYear = 3.15576e7;

    public static Nuclide Fe56 { get; } = new(26, 56);
    public static Nuclide C12 { get; } = new(6, 12);
    public static Nuclide O16 { get; } = new(8, 16);
    public static Nuclide Ne20 { get; } = new(10, 20);
    public static Nuclide Ne22 { get; } = new(10, 22);
    public static Nuclide Mg24 { get; } = new(12, 24);
    public static Nuclide Mg25 { get; } = new(12, 25);
    public static Nuclide Mg26 { get; } = new(12, 26);

    /// <summary>Light nuclei that absorb neutrons but are not part of the heavy-element network.</summary>
    public static IReadOnlyList<Nuclide> Poisons { get; } = [C12, O16, Ne20, Ne22, Mg24, Mg25, Mg26];

    /// <summary>Average core density during He burning (g/cm³): 1500·(M/15)^-0.8.</summary>
    public static double DefaultDensity(double stellarMass) => 1500.0 * Math.Pow(stellarMass / 15.0, -0.8);

    /// <summary>Core He-burning time (years): 1.9 Myr·(M/15)^-1.6.</summary>
    public static double DefaultHeliumBurningYears(double stellarMass) => 1.9e6 * Math.Pow(stellarMass / 15.0, -1.6);

    /// <summary>Initial mass fractions at the start of core He burning for metallicity <paramref name="z"/>.</summary>
    public static IReadOnlyDictionary<Nuclide, double> InitialMassFractions(double z) => new Dictionary<Nuclide, double>
    {
        [Ne22] = 1.0 * z,
        [Fe56] = 0.097 * z * 0.9175,
        [Ne20] = 0.095 * z,
        [Mg24] = 0.053 * z * 0.7899,
        [Mg25] = 0.053 * z * 0.1000,
        [Mg26] = 0.053 * z * 0.1101,
    };

    /// <summary>Initial helium mass fraction in the core: everything that is not metals.</summary>
    public static double InitialHelium(double z) => 1.0 - z;
}
