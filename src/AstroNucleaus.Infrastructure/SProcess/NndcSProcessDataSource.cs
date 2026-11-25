using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using AstroNucleaus.Application.Endf;
using AstroNucleaus.Application.SProcess;
using AstroNucleaus.Domain.SProcess;
using AstroNucleaus.Infrastructure.Endf;
using Microsoft.Extensions.Options;

namespace AstroNucleaus.Infrastructure.SProcess;

/// <summary>
/// s-process data from NNDC: Maxwellian-averaged (n,γ) cross sections from the ENDF/B-VII.1 tables (EAF-2010 for
/// nuclides missing there, mostly unstable branch points) and decay data from the ENDF/B-VIII.0 decay sub-library.
/// Both archives are downloaded once into the cache folder.
/// </summary>
public sealed partial class NndcSProcessDataSource : ISProcessDataSource
{
    /// <summary>Decay files are read from Fe (the seed) up; MACS are loaded for every nuclide, including light poisons.</summary>
    private const int MinZForDecay = 26;

    private readonly HttpClient _http;
    private readonly EndfOptions _options;
    private readonly Endf.Parsing.EndfParser _parser = new();

    public NndcSProcessDataSource(HttpClient http, IOptions<EndfOptions> options)
    {
        _http = http;
        _options = options.Value;
    }

    public async Task<SProcessNuclearData> LoadAsync(
        int maxZ, IProgress<SProcessProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var folder = Path.Combine(_options.CacheDirectory, "nndc", "astro");
        var macsPath = await EnsureDownloadedAsync(
            _options.MacsArchiveAddress, folder, "MACS tables", progress, cancellationToken).ConfigureAwait(false);
        var decayPath = await EnsureDownloadedAsync(
            _options.DecayArchiveAddress, folder, "decay data", progress, cancellationToken).ConfigureAwait(false);

        progress?.Report(new SProcessProgress("Reading nuclear data"));
        return await Task.Run(() => Build(macsPath, decayPath, maxZ, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    internal SProcessNuclearData Build(string macsArchive, string decayArchive, int maxZ, CancellationToken cancellationToken = default)
    {
        var (macs, endfCount, eafCount) = ReadMacs(macsArchive, cancellationToken);
        var decays = ReadDecays(decayArchive, MinZForDecay, maxZ, cancellationToken);

        var description =
            $"MACS: NNDC (Pritychenko & Mughabghab 2012) from ENDF/B-VII.1 ({endfCount} nuclides) + EAF-2010 ({eafCount} more); " +
            $"decay: ENDF/B-VIII.0 decay sub-library ({decays.Count} nuclides, Z {MinZForDecay}–{maxZ}).";
        return new SProcessNuclearData(macs, decays, description);
    }

    internal static (Dictionary<Nuclide, MaxwellianCrossSection> Macs, int EndfCount, int EafCount) ReadMacs(
        string archivePath, CancellationToken cancellationToken = default)
    {
        using var outer = ZipFile.OpenRead(archivePath);
        var endf = ReadLibrary(outer, "ENDFB71", cancellationToken);
        var eaf = ReadLibrary(outer, "EAF2010", cancellationToken);

        var result = new Dictionary<Nuclide, MaxwellianCrossSection>();
        foreach (var (nuclide, points) in endf)
            result[nuclide] = new MaxwellianCrossSection(points);

        var eafAdded = 0;
        foreach (var (nuclide, points) in eaf)
        {
            if (result.TryAdd(nuclide, new MaxwellianCrossSection(points)))
                eafAdded++;
        }

        return (result, endf.Count, eafAdded);
    }

    internal Dictionary<Nuclide, RadioactiveDecay> ReadDecays(
        string archivePath, int minZ, int maxZ, CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<Nuclide, RadioactiveDecay>();
        using var archive = ZipFile.OpenRead(archivePath);
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var match = DecayEntryRegex().Match(entry.Name);
            if (!match.Success)
                continue;

            var z = int.Parse(match.Groups["z"].Value, System.Globalization.CultureInfo.InvariantCulture);
            if (z < minZ || z > maxZ)
                continue;

            using var reader = new StreamReader(entry.Open(), Encoding.Latin1);
            var tape = _parser.Parse(reader);
            if (tape.Materials.FirstOrDefault()?.Decay is { } decay)
            {
                var a = int.Parse(match.Groups["a"].Value, System.Globalization.CultureInfo.InvariantCulture);
                result[new Nuclide(z, a)] = decay;
            }
        }

        return result;
    }

    private static Dictionary<Nuclide, List<(double, double)>> ReadLibrary(
        ZipArchive outer, string prefix, CancellationToken cancellationToken)
    {
        var points = new Dictionary<Nuclide, List<(double, double)>>();
        var innerEntry = outer.Entries.FirstOrDefault(e => e.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                                                           && e.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
        if (innerEntry is null)
            return points;

        using var buffer = new MemoryStream();
        using (var stream = innerEntry.Open())
            stream.CopyTo(buffer);
        buffer.Position = 0;

        using var inner = new ZipArchive(buffer, ZipArchiveMode.Read);
        foreach (var file in inner.Entries.Where(e => e.Name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var reader = new StreamReader(file.Open(), Encoding.Latin1);
            foreach (var (nuclide, kT, barns) in MacsTableParser.Parse(reader))
            {
                if (!points.TryGetValue(nuclide, out var list))
                    points[nuclide] = list = [];
                list.Add((kT, barns));
            }
        }

        return points;
    }

    private async Task<string> EnsureDownloadedAsync(
        Uri address, string folder, string what, IProgress<SProcessProgress>? progress, CancellationToken cancellationToken)
    {
        var path = Path.Combine(folder, Path.GetFileName(address.LocalPath));
        if (File.Exists(path))
            return path;

        Directory.CreateDirectory(folder);
        var partial = path + ".part";
        try
        {
            using var response = await _http.GetAsync(address, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    $"{address} returned {(int)response.StatusCode} {response.ReasonPhrase}.", null, response.StatusCode);
            }

            var total = response.Content.Headers.ContentLength;
            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var target = File.Create(partial))
            {
                var buffer = new byte[81920];
                long received = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    received += read;
                    progress?.Report(SProcessProgress.FromDownload(what, new DownloadProgress(received, total)));
                }
            }

            File.Move(partial, path, overwrite: true);
            return path;
        }
        catch
        {
            if (File.Exists(partial))
                File.Delete(partial);
            throw;
        }
    }

    [GeneratedRegex(@"^dec-(?<z>\d{3})_[A-Za-z]+_(?<a>\d{3})\.endf$", RegexOptions.IgnoreCase)]
    private static partial Regex DecayEntryRegex();
}
