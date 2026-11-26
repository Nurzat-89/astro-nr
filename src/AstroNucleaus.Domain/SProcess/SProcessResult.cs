namespace AstroNucleaus.Domain.SProcess;

/// <summary>Physical conditions used by a run.</summary>
/// <param name="DensityFromModel">True when the density came from <see cref="StellarCoreModel"/>, false when overridden.</param>
/// <param name="DurationYears">Whole core He-burning time.</param>
/// <param name="PhaseDurationYears">Length of the neutron-producing phase that is calculated.</param>
public sealed record SProcessConditions(
    double TemperatureGK,
    double KTkeV,
    double Density,
    bool DensityFromModel,
    double DurationYears,
    bool DurationFromModel,
    double PhaseDurationYears,
    double ThermalVelocity,
    double Ne22AlphaNRate,
    double Ne22AlphaGammaRate);

/// <summary>State of the core at one moment of the run.</summary>
/// <param name="NeutronDensity">Neutrons per cm³.</param>
/// <param name="Exposure">Neutron exposure τ accumulated so far, in mb⁻¹.</param>
public sealed record SProcessSnapshot(
    double TimeYears,
    double HeliumMassFraction,
    double Ne22MassFraction,
    double NeutronDensity,
    double Exposure);

/// <summary>Abundance of one network nuclide.</summary>
/// <param name="FinalMassFraction">At the end of core He burning.</param>
/// <param name="DecayedMassFraction">After all nuclides with T½ &lt; 1 Myr have decayed.</param>
/// <param name="RelativeToInitialFe56">Final number abundance divided by the initial ⁵⁶Fe number abundance.</param>
public sealed record NuclideAbundance(
    Nuclide Nuclide,
    double InitialMassFraction,
    double FinalMassFraction,
    double DecayedMassFraction,
    double RelativeToInitialFe56,
    double DecayedRelativeToInitialFe56,
    double HalfLifeSeconds,
    double CaptureBarns)
{
    public bool IsStable => double.IsPositiveInfinity(HalfLifeSeconds);

    /// <summary>Final over initial mass fraction; infinite for nuclides that were not present initially.</summary>
    public double ProductionFactor => InitialMassFraction > 0 ? DecayedMassFraction / InitialMassFraction : double.PositiveInfinity;
}

public sealed record SProcessResult(
    SProcessParameters Parameters,
    SProcessConditions Conditions,
    double NeutronExposure,
    double PeakNeutronDensity,
    double MeanNeutronDensity,
    double Ne22BurnedFraction,
    double Fe56DestroyedFraction,
    double MassFractionBeyondNetwork,
    int StepsTaken,
    IReadOnlyList<SProcessSnapshot> History,
    IReadOnlyList<NuclideAbundance> Abundances,
    string DataDescription)
{
    /// <summary>
    /// Abundances Y/Y₀(⁵⁶Fe) of all network nuclides at each <see cref="History"/> entry
    /// (frame k, index i ↔ <see cref="Abundances"/>[i]), for animations.
    /// </summary>
    public IReadOnlyList<double[]> HistoryAbundances { get; init; } = [];
}
