namespace AstroNucleaus.Application.Endf;

/// <summary>
/// Lists the evaluated libraries, sub-libraries, and files available from one ENDF source.
/// </summary>
public interface IEndfLibraryCatalog
{
    EndfSourceInfo Source { get; }

    /// <summary>Library names, e.g. "ENDF-B-VIII.0", "JEFF-4.0".</summary>
    Task<IReadOnlyList<string>> GetLibrariesAsync(CancellationToken cancellationToken = default);

    /// <summary>Sub-library names of a library, e.g. "neutrons", "protons", "decay".</summary>
    Task<IReadOnlyList<string>> GetSublibrariesAsync(string library, CancellationToken cancellationToken = default);

    /// <summary>Evaluation files of a sub-library.</summary>
    Task<IReadOnlyList<EndfRemoteFile>> GetFilesAsync(string library, string sublibrary, CancellationToken cancellationToken = default);
}
