namespace AstroNucleaus.Domain.Endf;

/// <summary>
/// General information about an evaluation, read from File 1, section 451 (MF=1, MT=451).
/// </summary>
public sealed record EndfMaterialDescription
{
    /// <summary>NLIB: library identifier.</summary>
    public int LibraryNumber { get; init; }

    /// <summary>NVER: library version.</summary>
    public int LibraryVersion { get; init; }

    /// <summary>LREL: library release number.</summary>
    public int ReleaseNumber { get; init; }

    /// <summary>NMOD: modification number of this material.</summary>
    public int ModificationNumber { get; init; }

    /// <summary>NSUB: sub-library number (projectile and data type).</summary>
    public int SubLibraryNumber { get; init; }

    /// <summary>NFOR: ENDF format version (6 for ENDF-6).</summary>
    public int FormatVersion { get; init; }

    /// <summary>LRP: resonance parameter flag.</summary>
    public int ResonanceParameterFlag { get; init; }

    /// <summary>LFI: true when the material is fissionable.</summary>
    public bool IsFissionable { get; init; }

    /// <summary>ELIS: excitation energy of the target (eV).</summary>
    public double ExcitationEnergy { get; init; }

    /// <summary>STA: true when the target is stable.</summary>
    public bool IsStable { get; init; }

    /// <summary>LIS: state number of the target nucleus.</summary>
    public int ExcitedStateNumber { get; init; }

    /// <summary>LISO: isomeric state number.</summary>
    public int IsomerNumber { get; init; }

    /// <summary>AWI: projectile mass in neutron masses.</summary>
    public double ProjectileMassRatio { get; init; }

    /// <summary>EMAX: upper energy limit of the evaluation (eV).</summary>
    public double MaxEnergy { get; init; }

    /// <summary>TEMP: target temperature (K).</summary>
    public double Temperature { get; init; }

    /// <summary>ZSYMAM: e.g. " 92-U -235 ".</summary>
    public string SymbolAndMass { get; init; } = string.Empty;

    /// <summary>ALAB: originating laboratory.</summary>
    public string Laboratory { get; init; } = string.Empty;

    /// <summary>EDATE: evaluation date.</summary>
    public string EvaluationDate { get; init; } = string.Empty;

    /// <summary>AUTH: authors.</summary>
    public string Authors { get; init; } = string.Empty;

    /// <summary>REF: primary reference.</summary>
    public string Reference { get; init; } = string.Empty;

    /// <summary>DDATE: distribution date.</summary>
    public string DistributionDate { get; init; } = string.Empty;

    /// <summary>RDATE: revision date.</summary>
    public string RevisionDate { get; init; } = string.Empty;

    /// <summary>ENDATE: master file entry date (yyyymmdd).</summary>
    public string MasterFileDate { get; init; } = string.Empty;

    /// <summary>All descriptive text lines of the section.</summary>
    public IReadOnlyList<string> Comments { get; init; } = [];

    public string CommentText => string.Join(Environment.NewLine, Comments);

    public string LibraryName => LibraryNumber switch
    {
        0 => "ENDF/B",
        1 => "ENDF/A",
        2 => "JEFF",
        3 => "EFF",
        4 => "ENDF/B High Energy",
        5 => "CENDL",
        6 => "JENDL",
        17 => "TENDL",
        18 => "ROSFOND",
        31 => "INDL/V",
        32 => "INDL/A",
        33 => "FENDL",
        34 => "IRDF",
        35 => "BROND",
        36 => "INGDB-90",
        37 => "FENDL/A",
        _ => $"NLIB {LibraryNumber}",
    };

    public string SubLibraryName => SubLibraryNumber switch
    {
        0 => "Photo-nuclear",
        1 => "Photo-induced fission product yields",
        3 => "Photo-atomic interaction",
        4 => "Radioactive decay",
        5 => "Spontaneous fission product yields",
        6 => "Atomic relaxation",
        10 => "Incident neutron",
        11 => "Neutron-induced fission product yields",
        12 => "Thermal neutron scattering",
        19 => "Neutron standards",
        113 => "Electro-atomic interaction",
        10010 => "Incident proton",
        10020 => "Incident deuteron",
        10030 => "Incident triton",
        20030 => "Incident ³He",
        20040 => "Incident α",
        _ => $"NSUB {SubLibraryNumber}",
    };
}
