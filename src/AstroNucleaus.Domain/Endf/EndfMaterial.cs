namespace AstroNucleaus.Domain.Endf;

/// <summary>
/// One evaluated material (MAT) of an ENDF tape.
/// </summary>
public sealed class EndfMaterial
{
    public EndfMaterial(
        int mat,
        double za,
        double atomicWeightRatio,
        EndfMaterialDescription? description,
        IReadOnlyList<CrossSection> crossSections,
        IReadOnlyList<EndfSectionInfo> sections)
    {
        Mat = mat;
        ZA = za;
        AtomicWeightRatio = atomicWeightRatio;
        Description = description;
        CrossSections = crossSections;
        Sections = sections;
    }

    /// <summary>MAT: material number.</summary>
    public int Mat { get; }

    /// <summary>ZA = 1000 * Z + A.</summary>
    public double ZA { get; }

    /// <summary>AWR: target mass in neutron masses.</summary>
    public double AtomicWeightRatio { get; }

    public EndfMaterialDescription? Description { get; }

    public IReadOnlyList<CrossSection> CrossSections { get; }

    public IReadOnlyList<EndfSectionInfo> Sections { get; }

    public int Z => (int)Math.Round(ZA) / 1000;

    public int MassNumber => (int)Math.Round(ZA) % 1000;

    public string Name =>
        Description is { SymbolAndMass.Length: > 0 } d ? d.SymbolAndMass.Trim() : $"MAT {Mat}";

    public CrossSection? GetCrossSection(int mt) => CrossSections.FirstOrDefault(x => x.MT == mt);

    public override string ToString() => $"{Name} (MAT {Mat})";
}
