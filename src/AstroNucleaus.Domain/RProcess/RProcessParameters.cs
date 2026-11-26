using AstroNucleaus.Domain.SProcess;

namespace AstroNucleaus.Domain.RProcess;

/// <summary>
/// Classical r-process conditions: constant temperature and neutron density for a given time, then freeze-out.
/// </summary>
public sealed record RProcessParameters
{
    /// <summary>Temperature in GK (typically 1–1.5).</summary>
    public required double TemperatureGK { get; init; }

    /// <summary>Neutron number density in cm⁻³ (typically 10²⁰–10²⁸).</summary>
    public required double NeutronDensity { get; init; }

    /// <summary>Duration of the neutron irradiation in seconds (typically 1–3 s).</summary>
    public required double DurationSeconds { get; init; }

    /// <summary>Seed nucleus.</summary>
    public Nuclide Seed { get; init; } = new(26, 56);

    /// <summary>Heaviest element in the network; β decays beyond it leave the network (fission is not modelled).</summary>
    public int MaxZ { get; init; } = 92;

    /// <summary>Time steps of the irradiation phase.</summary>
    public int TimeSteps { get; init; } = 1000;

    /// <summary>How long the products decay after freeze-out, in years.</summary>
    public double DecayYears { get; init; } = 1e9;

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (!(TemperatureGK is >= 0.1 and <= 5))
            errors.Add("Temperature must be between 0.1 and 5 GK.");
        if (!(NeutronDensity is >= 1e10 and <= 1e34))
            errors.Add("Neutron density must be between 1e10 and 1e34 cm⁻³.");
        if (!(DurationSeconds is > 0 and <= 1e6))
            errors.Add("Duration must be between 0 and 10⁶ s.");
        if (!(MaxZ is >= 30 and <= 110) || MaxZ <= Seed.Z)
            errors.Add("The network must end between Z = 30 and Z = 110, above the seed.");
        if (!(TimeSteps is >= 50 and <= 1_000_000))
            errors.Add("Time steps must be between 50 and 1,000,000.");
        if (!(DecayYears is >= 0 and <= 1e12))
            errors.Add("Decay time must be between 0 and 10¹² years.");
        return errors;
    }
}
