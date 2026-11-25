namespace AstroNucleaus.Domain.SProcess;

/// <summary>
/// Nuclear input for the s-process network: capture cross sections (MACS) and decay data per nuclide.
/// </summary>
public sealed class SProcessNuclearData
{
    public SProcessNuclearData(
        IReadOnlyDictionary<Nuclide, MaxwellianCrossSection> captureCrossSections,
        IReadOnlyDictionary<Nuclide, RadioactiveDecay> decays,
        string description)
    {
        CaptureCrossSections = captureCrossSections;
        Decays = decays;
        Description = description;
    }

    public IReadOnlyDictionary<Nuclide, MaxwellianCrossSection> CaptureCrossSections { get; }

    /// <summary>Decay data; a nuclide that is absent here but has a MACS is treated as stable.</summary>
    public IReadOnlyDictionary<Nuclide, RadioactiveDecay> Decays { get; }

    /// <summary>Where the data comes from, for display.</summary>
    public string Description { get; }

    public double CaptureBarns(Nuclide nuclide, double kTkeV) =>
        CaptureCrossSections.TryGetValue(nuclide, out var macs) ? macs.Evaluate(kTkeV) : 0.0;
}
