using AstroNucleaus.Domain.Endf;

namespace AstroNucleaus.Application.Endf;

/// <summary>
/// Use cases for browsing remote ENDF libraries, downloading evaluations, and parsing them.
/// </summary>
public sealed class EndfLibraryService
{
    private readonly IReadOnlyList<IEndfLibraryCatalog> _catalogs;
    private readonly IEndfFileDownloader _downloader;
    private readonly IEndfParser _parser;

    public EndfLibraryService(
        IEnumerable<IEndfLibraryCatalog> catalogs,
        IEndfFileDownloader downloader,
        IEndfParser parser)
    {
        _catalogs = catalogs.ToList();
        _downloader = downloader;
        _parser = parser;
    }

    /// <summary>Available sources, in order of preference.</summary>
    public IReadOnlyList<EndfSourceInfo> Sources => _catalogs.Select(c => c.Source).ToList();

    public Task<IReadOnlyList<string>> GetLibrariesAsync(string sourceId, CancellationToken cancellationToken = default) =>
        GetCatalog(sourceId).GetLibrariesAsync(cancellationToken);

    public Task<IReadOnlyList<string>> GetSublibrariesAsync(
        string sourceId, string library, CancellationToken cancellationToken = default) =>
        GetCatalog(sourceId).GetSublibrariesAsync(library, cancellationToken);

    public Task<IReadOnlyList<EndfRemoteFile>> GetFilesAsync(
        string sourceId, string library, string sublibrary, CancellationToken cancellationToken = default) =>
        GetCatalog(sourceId).GetFilesAsync(library, sublibrary, cancellationToken);

    /// <summary>Downloads (or takes from cache) an evaluation and parses it.</summary>
    public async Task<EndfTape> DownloadAndParseAsync(
        EndfRemoteFile file,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var path = await _downloader.DownloadAsync(file, progress, cancellationToken).ConfigureAwait(false);
        return await ParseLocalFileAsync(path, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Parses an ENDF file from disk on a background thread.</summary>
    public Task<EndfTape> ParseLocalFileAsync(string path, CancellationToken cancellationToken = default) =>
        Task.Run(() => _parser.ParseFile(path), cancellationToken);

    private IEndfLibraryCatalog GetCatalog(string sourceId) =>
        _catalogs.FirstOrDefault(c => c.Source.Id == sourceId)
        ?? throw new ArgumentException($"Unknown ENDF source '{sourceId}'.", nameof(sourceId));
}
