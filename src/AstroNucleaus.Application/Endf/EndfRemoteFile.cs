namespace AstroNucleaus.Application.Endf;

/// <summary>
/// An evaluation available from a remote ENDF source.
/// It is either a file of its own (<see cref="ArchiveEntry"/> is null), e.g. "n_026-Fe-56_2631.zip",
/// or one entry of a remote zip archive that holds a whole sub-library, e.g. "n-026_Fe_056.endf".
/// The nuclide fields are filled when the name follows a known naming convention.
/// </summary>
/// <param name="Library">Library name, e.g. "ENDF-B-VIII.0".</param>
/// <param name="Sublibrary">Sub-library name, e.g. "neutrons".</param>
/// <param name="FileName">File name of the evaluation.</param>
/// <param name="Uri">URL of the file, or of the archive that contains it.</param>
public sealed record EndfRemoteFile(string Library, string Sublibrary, string FileName, Uri Uri)
{
    /// <summary>Identifier of the source this file comes from.</summary>
    public string SourceId { get; init; } = string.Empty;

    /// <summary>Full entry name inside the archive at <see cref="Uri"/>, or null for a standalone file.</summary>
    public string? ArchiveEntry { get; init; }

    /// <summary>Uncompressed size in bytes, when known.</summary>
    public long? Size { get; init; }

    /// <summary>Projectile or data type prefix, e.g. "n", "p", "dec".</summary>
    public string? Projectile { get; init; }

    public int? Z { get; init; }

    public string? Symbol { get; init; }

    /// <summary>Mass number as written in the name ("0" for natural elements).</summary>
    public string? MassNumber { get; init; }

    /// <summary>Isomer tag, e.g. "M" or "m1".</summary>
    public string? Isomer { get; init; }

    public int? Mat { get; init; }

    public string DisplayName =>
        Symbol is null ? FileName : $"{Symbol}-{MassNumber}{Isomer}";
}
