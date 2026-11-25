using System.Net;
using System.Text.RegularExpressions;
using AstroNucleaus.Application.Endf;

namespace AstroNucleaus.Infrastructure.Endf.Download;

/// <summary>
/// ENDF libraries of the IAEA Nuclear Data Section (or a mirror with the same layout), read from
/// the HTML directory index at https://www-nds.iaea.org/public/download-endf/.
/// </summary>
public sealed partial class IaeaEndfLibraryCatalog : IEndfLibraryCatalog
{
    public static readonly EndfSourceInfo SourceInfo = new("iaea", "IAEA Nuclear Data Section");

    private readonly HttpClient _http;

    public IaeaEndfLibraryCatalog(HttpClient http)
    {
        _http = http;
    }

    public EndfSourceInfo Source => SourceInfo;

    public async Task<IReadOnlyList<string>> GetLibrariesAsync(CancellationToken cancellationToken = default)
    {
        var html = await GetIndexAsync(string.Empty, cancellationToken).ConfigureAwait(false);
        return ParseDirectoryNames(html);
    }

    public async Task<IReadOnlyList<string>> GetSublibrariesAsync(string library, CancellationToken cancellationToken = default)
    {
        var html = await GetIndexAsync(DirectoryPath(library), cancellationToken).ConfigureAwait(false);
        return ParseDirectoryNames(html);
    }

    public async Task<IReadOnlyList<EndfRemoteFile>> GetFilesAsync(
        string library, string sublibrary, CancellationToken cancellationToken = default)
    {
        var directory = DirectoryPath(library, sublibrary);
        var html = await GetIndexAsync(directory, cancellationToken).ConfigureAwait(false);
        var directoryUri = new Uri(_http.BaseAddress!, directory);

        return ParseFileNames(html, ".zip")
            .Select(name => EndfFileName.Parse(library, sublibrary, name, new Uri(directoryUri, Uri.EscapeDataString(name)))
                with { SourceId = SourceInfo.Id })
            .ToList();
    }

    /// <summary>Sub-directory names linked from an Apache index page, without the trailing slash.</summary>
    internal static IReadOnlyList<string> ParseDirectoryNames(string html) =>
        GetRelativeLinks(html)
            .Where(href => href.EndsWith('/'))
            .Select(href => href.TrimEnd('/'))
            .Where(name => name.Length > 0 && !name.Contains('/'))
            .Distinct(StringComparer.Ordinal)
            .ToList();

    /// <summary>File names with <paramref name="extension"/> linked from an Apache index page.</summary>
    internal static IReadOnlyList<string> ParseFileNames(string html, string extension) =>
        GetRelativeLinks(html)
            .Where(href => !href.Contains('/') && href.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.Ordinal)
            .ToList();

    private static IEnumerable<string> GetRelativeLinks(string html) =>
        HrefRegex().Matches(html)
            .Select(m => WebUtility.HtmlDecode(Uri.UnescapeDataString(m.Groups["href"].Value)))
            .Where(href => !href.StartsWith('/') && !href.StartsWith("..", StringComparison.Ordinal) && !href.Contains(':'));

    private async Task<string> GetIndexAsync(string relativePath, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(relativePath, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"{new Uri(_http.BaseAddress!, relativePath)} returned {(int)response.StatusCode} {response.ReasonPhrase}.",
                null,
                response.StatusCode);
        }

        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string DirectoryPath(params string[] segments) =>
        string.Concat(segments.Select(s => Uri.EscapeDataString(s) + "/"));

    [GeneratedRegex("href\\s*=\\s*\"(?<href>[^\"?#]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex HrefRegex();
}
