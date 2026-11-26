using AstroNucleaus.Domain.SProcess;

namespace AstroNucleaus.Domain.RProcess;

/// <summary>Abundance of one nuclide, normalised to the initial seed (Y / Y₀(seed)).</summary>
/// <param name="AtFreezeOut">At the end of the neutron irradiation.</param>
/// <param name="Decayed">After the decay time.</param>
/// <param name="NeutronSeparationMeV">S_n used for photodisintegration, when known.</param>
public sealed record RProcessAbundance(
    Nuclide Nuclide,
    double AtFreezeOut,
    double Decayed,
    double BetaHalfLifeSeconds,
    double? NeutronSeparationMeV);

/// <summary>Abundance summed over isobars of mass number A.</summary>
public sealed record MassChainAbundance(int A, double AtFreezeOut, double Decayed);

/// <summary>Average state of the matter during the irradiation.</summary>
public sealed record RProcessSnapshot(double TimeSeconds, double MeanZ, double MeanA, double SeedRemaining);

/// <summary>One recorded moment for animations.</summary>
/// <param name="TimeSeconds">Time since the start of the irradiation (the decay phase continues after it).</param>
/// <param name="IsIrradiation">True during the neutron irradiation, false during the decay after freeze-out.</param>
public sealed record RProcessFrameInfo(double TimeSeconds, bool IsIrradiation, double MeanZ, double MeanA);

public sealed record RProcessResult(
    RProcessParameters Parameters,
    double KTkeV,
    int NetworkSize,
    int PhotodisintegrationFromMasses,
    int PhotodisintegrationFromReaclib,
    double NeutronsCapturedPerSeed,
    double MeanAAtFreezeOut,
    double MeanADecayed,
    double LostBeyondMaxZ,
    IReadOnlyList<RProcessSnapshot> History,
    IReadOnlyList<RProcessAbundance> Abundances,
    IReadOnlyList<MassChainAbundance> MassChains,
    IReadOnlyList<Nuclide> WaitingPoints,
    IReadOnlyList<string> Warnings,
    string DataDescription)
{
    /// <summary>Recorded moments of the irradiation and the decay phase.</summary>
    public IReadOnlyList<RProcessFrameInfo> FrameInfos { get; init; } = [];

    /// <summary>Y/Y₀(seed) at each frame; index i ↔ <see cref="Abundances"/>[i].</summary>
    public IReadOnlyList<double[]> FrameAbundances { get; init; } = [];
}
