using System.IO.Compression;
using AstroNucleaus.Application.Endf;
using Microsoft.Extensions.Options;

namespace AstroNucleaus.Infrastructure.Endf.Download;

/// <summary>
/// Downloads evaluations over HTTP into {CacheDirectory}/{source}/{library}/{sublibrary}/{file name without extension}/.
/// Standalone files are downloaded and unzipped; entries of remote archives are fetched alone with range requests.
/// Files that are already in the cache are not downloaded again.
/// </summary>
public sealed class HttpEndfFileDownloader : IEndfFileDownloader
{
    private static readonly string[] PreferredExtensions = [".endf", ".endf6", ".dat", ".txt", ".asc"];

    private readonly HttpClient _http;
    private readonly RemoteZipArchive _remoteZip;
    private readonly EndfOptions _options;

    public HttpEndfFileDownloader(HttpClient http, IOptions<EndfOptions> options)
    {
        _http = http;
        _remoteZip = new RemoteZipArchive(http);
        _options = options.Value;
    }

    public async Task<string> DownloadAsync(
        EndfRemoteFile file,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);

        var targetDirectory = Path.Combine(
            _options.CacheDirectory,
            SafeName(file.SourceId.Length > 0 ? file.SourceId : "other"),
            SafeName(file.Library),
            SafeName(file.Sublibrary),
            SafeName(Path.GetFileNameWithoutExtension(file.FileName)));

        if (FindEndfFile(targetDirectory) is { } cached)
        {
            var size = new FileInfo(cached).Length;
            progress?.Report(new DownloadProgress(size, size));
            return cached;
        }

        // Work in a temporary folder so that an interrupted download never looks like a cached file.
        var workDirectory = targetDirectory + ".download";
        if (Directory.Exists(workDirectory))
            Directory.Delete(workDirectory, recursive: true);
        Directory.CreateDirectory(workDirectory);

        try
        {
            if (file.ArchiveEntry is { } entryName)
                await ExtractRemoteEntryAsync(file, entryName, workDirectory, progress, cancellationToken).ConfigureAwait(false);
            else
                await DownloadStandaloneFileAsync(file, workDirectory, progress, cancellationToken).ConfigureAwait(false);

            if (FindEndfFile(workDirectory) is null)
                throw new InvalidDataException($"{file.FileName} does not contain an ENDF file.");

            if (Directory.Exists(targetDirectory))
                Directory.Delete(targetDirectory, recursive: true);
            Directory.Move(workDirectory, targetDirectory);
        }
        catch
        {
            TryDeleteDirectory(workDirectory);
            throw;
        }

        return FindEndfFile(targetDirectory)!;
    }

    private async Task DownloadStandaloneFileAsync(
        EndfRemoteFile file, string workDirectory, IProgress<DownloadProgress>? progress, CancellationToken cancellationToken)
    {
        var archivePath = Path.Combine(workDirectory, SafeName(file.FileName));
        await DownloadToFileAsync(file.Uri, archivePath, progress, cancellationToken).ConfigureAwait(false);

        if (IsZipArchive(archivePath))
        {
            ZipFile.ExtractToDirectory(archivePath, workDirectory, overwriteFiles: true);
            File.Delete(archivePath);
        }
        else
        {
            File.Move(archivePath, Path.ChangeExtension(archivePath, ".endf"));
        }
    }

    private async Task ExtractRemoteEntryAsync(
        EndfRemoteFile file,
        string entryName,
        string workDirectory,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var entries = await _remoteZip.GetEntriesAsync(file.Uri, cancellationToken).ConfigureAwait(false);
        var entry = entries.FirstOrDefault(e => e.Name == entryName)
                    ?? throw new InvalidDataException($"{entryName} was not found in {file.Uri}.");

        var path = Path.Combine(workDirectory, SafeName(Path.GetFileName(entryName)));
        await using var output = File.Create(path);
        await _remoteZip.ExtractAsync(file.Uri, entry, output, progress, cancellationToken).ConfigureAwait(false);
    }

    private async Task DownloadToFileAsync(
        Uri uri, string path, IProgress<DownloadProgress>? progress, CancellationToken cancellationToken)
    {
        using var response = await _http
            .GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"{uri} returned {(int)response.StatusCode} {response.ReasonPhrase}.", null, response.StatusCode);
        }

        var total = response.Content.Headers.ContentLength;
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var target = File.Create(path);

        var buffer = new byte[81920];
        long received = 0;
        int read;
        progress?.Report(new DownloadProgress(0, total));
        while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            received += read;
            progress?.Report(new DownloadProgress(received, total));
        }
    }

    private static string? FindEndfFile(string directory)
    {
        if (!Directory.Exists(directory))
            return null;

        return Directory
            .EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Where(f => !f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f =>
            {
                var index = Array.IndexOf(PreferredExtensions, Path.GetExtension(f).ToLowerInvariant());
                return index < 0 ? int.MaxValue : index;
            })
            .ThenByDescending(f => new FileInfo(f).Length)
            .FirstOrDefault();
    }

    private static bool IsZipArchive(string path)
    {
        Span<byte> signature = stackalloc byte[4];
        using var stream = File.OpenRead(path);
        return stream.Read(signature) == 4
            && signature[0] == (byte)'P' && signature[1] == (byte)'K' && signature[2] == 3 && signature[3] == 4;
    }

    private static string SafeName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        var safe = new string(chars).Trim('.', ' ');
        return safe.Length > 0 ? safe : "_";
    }

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
            // Leftovers are cleaned up on the next download of the same file.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
