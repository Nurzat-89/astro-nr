using AstroNucleaus.Application.Endf;
using Microsoft.Extensions.Options;

namespace AstroNucleaus.Infrastructure.Endf.Download;

/// <summary>
/// ENDF libraries of the National Nuclear Data Center (Brookhaven). Each sub-library is a zip archive;
/// the list of evaluations is read from the archive's central directory with HTTP range requests.
/// </summary>
public sealed class NndcEndfLibraryCatalog : IEndfLibraryCatalog
{
    public static readonly EndfSourceInfo SourceInfo = new("nndc", "NNDC (Brookhaven)");

    private static readonly string[] NonEvaluationExtensions = [".txt", ".pdf", ".md", ".html", ".htm", ".list"];

    private readonly RemoteZipArchive _archive;
    private readonly IReadOnlyList<NndcLibraryOptions> _libraries;

    public NndcEndfLibraryCatalog(HttpClient http, IOptions<EndfOptions> options)
    {
        _archive = new RemoteZipArchive(http);
        _libraries = options.Value.NndcLibraries;
    }

    public EndfSourceInfo Source => SourceInfo;

    public Task<IReadOnlyList<string>> GetLibrariesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<string>>(_libraries.Select(l => l.Name).ToList());

    public Task<IReadOnlyList<string>> GetSublibrariesAsync(string library, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<string>>(GetLibrary(library).Sublibraries.ToList());

    public async Task<IReadOnlyList<EndfRemoteFile>> GetFilesAsync(
        string library, string sublibrary, CancellationToken cancellationToken = default)
    {
        var archiveUri = GetLibrary(library).GetArchiveUri(sublibrary);
        var entries = await _archive.GetEntriesAsync(archiveUri, cancellationToken).ConfigureAwait(false);

        return entries
            .Where(IsEvaluation)
            .Select(e => EndfFileName.ParseArchiveEntry(library, sublibrary, archiveUri, e.Name) with
            {
                SourceId = SourceInfo.Id,
                Size = e.UncompressedSize,
            })
            .OrderBy(f => f.Z ?? int.MaxValue)
            .ThenBy(f => int.TryParse(f.MassNumber, out var a) ? a : int.MaxValue)
            .ThenBy(f => f.FileName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsEvaluation(RemoteZipEntry entry) =>
        !entry.IsDirectory
        && !NonEvaluationExtensions.Contains(Path.GetExtension(entry.Name), StringComparer.OrdinalIgnoreCase);

    private NndcLibraryOptions GetLibrary(string name) =>
        _libraries.FirstOrDefault(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase))
        ?? throw new ArgumentException($"Unknown NNDC library '{name}'.", nameof(name));
}
