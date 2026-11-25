namespace AstroNucleaus.Domain.SProcess;

/// <summary>
/// Input of a weak s-process calculation in the convective He-burning core of a massive star.
/// </summary>
public sealed record SProcessParameters
{
    /// <summary>Initial stellar mass in solar masses.</summary>
    public required double StellarMass { get; init; }

    /// <summary>Average core temperature during He burning, in GK (typically 0.2–0.35).</summary>
    public required double TemperatureGK { get; init; }

    /// <summary>Initial metallicity Z (mass fraction of elements heavier than He). Solar ≈ 0.014.</summary>
    public double Metallicity { get; init; } = 0.014;

    /// <summary>Average core density in g/cm³; null uses <see cref="StellarCoreModel.DefaultDensity"/>.</summary>
    public double? DensityOverride { get; init; }

    /// <summary>Core He-burning time in years; null uses <see cref="StellarCoreModel.DefaultHeliumBurningYears"/>.</summary>
    public double? DurationYearsOverride { get; init; }

    /// <summary>Mass fraction of burnt helium that ends up as ¹²C (the rest as ¹⁶O).</summary>
    public double CarbonFractionOfAsh { get; init; } = 0.3;

    /// <summary>
    /// Central He mass fraction at which the neutron-producing phase starts. ²²Ne(α,n) needs the higher
    /// temperatures reached near the end of core He burning, so the calculation covers the phase from this
    /// He fraction down to zero, with the C/O ash of the earlier burning already present.
    /// </summary>
    public double HeliumAtOnset { get; init; } = 0.1;

    /// <summary>Heaviest element in the network (Z = 42 is Mo).</summary>
    public int MaxZ { get; init; } = 42;

    /// <summary>Number of time steps (more steps are added automatically when the neutron flux is high).</summary>
    public int TimeSteps { get; init; } = 2000;

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (!(StellarMass is >= 8 and <= 120))
            errors.Add("Stellar mass must be between 8 and 120 solar masses (massive stars).");
        if (!(TemperatureGK is >= 0.05 and <= 1.5))
            errors.Add("Temperature must be between 0.05 and 1.5 GK.");
        if (!(Metallicity is > 0 and <= 0.1))
            errors.Add("Metallicity must be between 0 and 0.1.");
        if (DensityOverride is { } rho && !(rho > 0))
            errors.Add("Density must be positive.");
        if (DurationYearsOverride is { } years && !(years > 0))
            errors.Add("Duration must be positive.");
        if (!(CarbonFractionOfAsh is >= 0 and <= 1))
            errors.Add("Carbon fraction of the ash must be between 0 and 1.");
        if (!(HeliumAtOnset is > 0 and <= 1))
            errors.Add("Helium fraction at the onset must be between 0 and 1.");
        if (!(MaxZ is >= 30 and <= 60))
            errors.Add("The network must end between Z = 30 and Z = 60.");
        if (!(TimeSteps is >= 50 and <= 1_000_000))
            errors.Add("Time steps must be between 50 and 1,000,000.");
        return errors;
    }
}
