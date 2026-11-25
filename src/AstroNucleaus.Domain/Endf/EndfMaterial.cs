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

    /// <summary>Element symbol, e.g. "U"; null when Z is unknown.</summary>
    public string? Symbol => ZA >= 1 ? ChemicalElements.GetSymbol(Z) : null;

    /// <summary>
    /// Material name, e.g. "92-U-235": ZSYMAM without blanks when present, otherwise built from ZA.
    /// </summary>
    public string Name
    {
        get
        {
            if (Description is { SymbolAndMass.Length: > 0 } d)
                return d.SymbolAndMass.Replace(" ", string.Empty, StringComparison.Ordinal);

            return Symbol is { } symbol
                ? $"{Z}-{symbol}-{(MassNumber == 0 ? "nat" : MassNumber.ToString(System.Globalization.CultureInfo.InvariantCulture))}"
                : $"MAT {Mat}";
        }
    }

    public CrossSection? GetCrossSection(int mt) => CrossSections.FirstOrDefault(x => x.MT == mt);

    public override string ToString() => $"{Name} (MAT {Mat})";
}
