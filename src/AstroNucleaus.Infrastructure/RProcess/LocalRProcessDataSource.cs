using System.Text;
using System.Text.Json;
using AstroNucleaus.Application.Endf;
using AstroNucleaus.Application.RProcess;
using AstroNucleaus.Application.SProcess;
using AstroNucleaus.Domain.RProcess;
using AstroNucleaus.Domain.SProcess;
using AstroNucleaus.Infrastructure.Endf;
using Microsoft.Extensions.Options;

namespace AstroNucleaus.Infrastructure.RProcess;

/// <summary>
/// r-process data: REACLIB rates and an optional theoretical mass table imported by the user (copied into
/// {cache}/rprocess), AME2020 masses and NUBASE2020 spins downloaded from the IAEA Atomic Mass Data Center.
/// AME2020 values take precedence; the theoretical table fills nuclides that AME2020 does not list.
/// </summary>
public sealed class LocalRProcessDataSource : IRProcessDataSource
{
    private const string ReaclibFileName = "reaclib.dat";
    private const string MassTableFileName = "masses-user.dat";
    private const string StatusFileName = "imports.json";

    private readonly HttpClient _http;
    private readonly EndfOptions _options;

    public LocalRProcessDataSource(HttpClient http, IOptions<EndfOptions> options)
    {
        _http = http;
        _options = options.Value;
    }

    private string Folder => Path.Combine(_options.CacheDirectory, "rprocess");

    public RProcessDataStatus GetStatus()
    {
        var info = ReadImports();
        return new RProcessDataStatus(
            File.Exists(Path.Combine(Folder, ReaclibFileName)) ? info.ReaclibFile ?? ReaclibFileName : null,
            info.ReaclibImported,
            File.Exists(Path.Combine(Folder, MassTableFileName)) ? info.MassTableFile ?? MassTableFileName : null,
            info.MassTableEntries);
    }

    public async Task ImportReaclibAsync(string path, CancellationToken cancellationToken = default)
    {
        await ValidateReaclibAsync(path, Path.GetFileName(path), cancellationToken).ConfigureAwait(false);

        Directory.CreateDirectory(Folder);
        File.Copy(path, Path.Combine(Folder, ReaclibFileName), overwrite: true);
        WriteImports(ReadImports() with { ReaclibFile = Path.GetFileName(path), ReaclibImported = DateTime.Now });
    }

    public async Task DownloadReaclibAsync(IProgress<SProcessProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var address = _options.ReaclibDownloadAddress;
        var name = $"{Path.GetFileName(address.LocalPath)} (pynucastro)";
        Directory.CreateDirectory(Folder);
        var downloaded = Path.Combine(Folder, "reaclib-download.tmp");
        try
        {
            await DownloadToFileAsync(address, downloaded, "REACLIB", progress, cancellationToken).ConfigureAwait(false);
            progress?.Report(new SProcessProgress("Checking the REACLIB file"));
            await ValidateReaclibAsync(downloaded, name, cancellationToken).ConfigureAwait(false);
            File.Move(downloaded, Path.Combine(Folder, ReaclibFileName), overwrite: true);
            WriteImports(ReadImports() with { ReaclibFile = name, ReaclibImported = DateTime.Now });
        }
        finally
        {
            if (File.Exists(downloaded))
                File.Delete(downloaded);
        }
    }

    private static async Task ValidateReaclibAsync(string path, string name, CancellationToken cancellationToken)
    {
        var (captures, _, decays) = await Task.Run(() => ReadReaclib(path, cancellationToken), cancellationToken).ConfigureAwait(false);
        if (captures.Count == 0)
            throw new InvalidDataException($"{name} contains no (n,γ) rates; is it a REACLIB file?");
        if (decays.Count == 0)
            throw new InvalidDataException($"{name} contains no β⁻ decay rates; the r-process needs the weak rates too.");
    }

    public async Task ImportMassTableAsync(string path, CancellationToken cancellationToken = default)
    {
        var table = await Task.Run(() =>
        {
            using var reader = new StreamReader(path, Encoding.Latin1);
            return MassTableParsers.ParseUserTable(reader);
        }, cancellationToken).ConfigureAwait(false);

        if (table.Count == 0)
            throw new InvalidDataException($"{Path.GetFileName(path)} contains no 'Z A mass-excess(MeV)' lines.");

        Directory.CreateDirectory(Folder);
        File.Copy(path, Path.Combine(Folder, MassTableFileName), overwrite: true);
        WriteImports(ReadImports() with { MassTableFile = Path.GetFileName(path), MassTableEntries = table.Count });
    }

    public void RemoveMassTable()
    {
        var file = Path.Combine(Folder, MassTableFileName);
        if (File.Exists(file))
            File.Delete(file);
        WriteImports(ReadImports() with { MassTableFile = null, MassTableEntries = null });
    }

    public async Task<RProcessNuclearData> LoadAsync(IProgress<SProcessProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var reaclibPath = Path.Combine(Folder, ReaclibFileName);
        if (!File.Exists(reaclibPath))
            throw new InvalidOperationException("Load a REACLIB file first (download it from reaclib.jinaweb.org in your browser).");

        var amdc = Path.Combine(_options.CacheDirectory, "amdc");
        var amePath = await DownloadOnceAsync(_options.Ame2020Address, amdc, "AME2020 masses", progress, cancellationToken).ConfigureAwait(false);
        var nubasePath = await DownloadOnceAsync(_options.Nubase2020Address, amdc, "NUBASE2020", progress, cancellationToken).ConfigureAwait(false);

        progress?.Report(new SProcessProgress("Reading REACLIB, masses and spins"));
        return await Task.Run(() => Build(reaclibPath, amePath, nubasePath, Path.Combine(Folder, MassTableFileName), cancellationToken), cancellationToken)
            .ConfigureAwait(false);
    }

    internal static RProcessNuclearData Build(string reaclibPath, string amePath, string nubasePath, string? userMassPath, CancellationToken cancellationToken = default)
    {
        var (captures, reverse, decays) = ReadReaclib(reaclibPath, cancellationToken);

        Dictionary<Nuclide, double> masses;
        using (var reader = new StreamReader(amePath, Encoding.Latin1))
            masses = MassTableParsers.ParseAme2020(reader);
        var ameCount = masses.Count;

        var theoryAdded = 0;
        if (userMassPath is not null && File.Exists(userMassPath))
        {
            using var reader = new StreamReader(userMassPath, Encoding.Latin1);
            foreach (var (nuclide, mev) in MassTableParsers.ParseUserTable(reader))
            {
                if (masses.TryAdd(nuclide, mev))
                    theoryAdded++;
            }
        }

        Dictionary<Nuclide, double> spins;
        using (var reader = new StreamReader(nubasePath, Encoding.UTF8))
            spins = MassTableParsers.ParseNubaseSpins(reader);

        var description =
            $"REACLIB: {captures.Count} (n,γ), {decays.Count} β⁻ decays, {reverse.Count} (γ,n) reverse rates; " +
            $"masses: AME2020 ({ameCount})" + (theoryAdded > 0 ? $" + theoretical table ({theoryAdded} more)" : " only") +
            $"; spins: NUBASE2020 ({spins.Count}).";

        return new RProcessNuclearData(masses, spins, captures, reverse, decays, description);
    }

    internal static (Dictionary<Nuclide, ReaclibRate> Captures, Dictionary<Nuclide, ReaclibRate> Reverse,
        Dictionary<Nuclide, IReadOnlyList<BetaDecayChannel>> Decays) ReadReaclib(string path, CancellationToken cancellationToken = default)
    {
        var captureSets = new Dictionary<Nuclide, List<double[]>>();
        var reverseSets = new Dictionary<Nuclide, List<double[]>>();
        var decaySets = new Dictionary<(Nuclide Parent, Nuclide Daughter, int Neutrons), (List<double[]> Sets, string Label)>();
        var labels = new Dictionary<Nuclide, string>();

        using var reader = new StreamReader(path, Encoding.Latin1);
        var count = 0;
        foreach (var set in ReaclibParser.Parse(reader))
        {
            if (++count % 10000 == 0)
                cancellationToken.ThrowIfCancellationRequested();

            var neutrons = set.Products.Count(p => p == ReaclibParser.Neutron);
            var heavyProducts = set.Products.Where(p => p != ReaclibParser.Neutron).ToList();

            // (n,γ): n + (Z,A) → (Z,A+1)
            if (set.Chapter == 4 && !set.IsReverse && set.Reactants.Contains(ReaclibParser.Neutron) && heavyProducts.Count == 1)
            {
                var target = set.Reactants.First(r => r != ReaclibParser.Neutron);
                if (heavyProducts[0] == target with { A = target.A + 1 })
                    Add(captureSets, target, set.Coefficients, labels, set.Label);
                continue;
            }

            // (γ,n) reverse: (Z,A) → (Z,A−1) + n
            if (set.Chapter == 2 && set.IsReverse && !set.IsWeak && neutrons == 1 && heavyProducts.Count == 1)
            {
                var parent = set.Reactants[0];
                if (heavyProducts[0] == parent with { A = parent.A - 1 })
                    Add(reverseSets, parent, set.Coefficients, null, null);
                continue;
            }

            // β⁻ with 0, 1 or 2 delayed neutrons: (Z,A) → (Z+1, A−k) + k n
            if (set.IsWeak && set.Chapter is 1 or 2 or 3 && heavyProducts.Count == 1)
            {
                var parent = set.Reactants[0];
                var daughter = heavyProducts[0];
                if (daughter.Z == parent.Z + 1 && daughter.A == parent.A - neutrons)
                {
                    var key = (parent, daughter, neutrons);
                    if (!decaySets.TryGetValue(key, out var entry))
                        decaySets[key] = entry = ([], set.Label);
                    entry.Sets.Add(set.Coefficients);
                }
            }
        }

        var captures = captureSets.ToDictionary(p => p.Key, p => ToRate($"n,γ {p.Key}", labels[p.Key], p.Value));
        var reverse = reverseSets.ToDictionary(p => p.Key, p => ToRate($"γ,n {p.Key}", "rev", p.Value));
        var decays = decaySets
            .GroupBy(p => p.Key.Parent)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<BetaDecayChannel>)g
                    .Select(p => new BetaDecayChannel(p.Key.Daughter, p.Key.Neutrons, ToRate("β", p.Value.Label, p.Value.Sets).Evaluate(1.0), p.Value.Label))
                    .ToList());

        return (captures, reverse, decays);
    }

    private static void Add(Dictionary<Nuclide, List<double[]>> sets, Nuclide key, double[] coefficients,
        Dictionary<Nuclide, string>? labels, string? label)
    {
        if (!sets.TryGetValue(key, out var list))
            sets[key] = list = [];
        list.Add(coefficients);
        if (labels is not null && label is not null)
            labels.TryAdd(key, label);
    }

    private static ReaclibRate ToRate(string reaction, string label, List<double[]> sets) =>
        new(reaction, label, "JINA REACLIB", sets.ToArray());

    private async Task<string> DownloadOnceAsync(Uri address, string folder, string what, IProgress<SProcessProgress>? progress, CancellationToken cancellationToken)
    {
        var path = Path.Combine(folder, Path.GetFileName(address.LocalPath));
        if (File.Exists(path))
            return path;

        Directory.CreateDirectory(folder);
        await DownloadToFileAsync(address, path, what, progress, cancellationToken).ConfigureAwait(false);
        return path;
    }

    /// <summary>Downloads <paramref name="address"/> to <paramref name="path"/> via a ".part" file, reporting progress.</summary>
    private async Task DownloadToFileAsync(Uri address, string path, string what, IProgress<SProcessProgress>? progress, CancellationToken cancellationToken)
    {
        progress?.Report(new SProcessProgress($"Downloading {what}"));
        using var response = await _http.GetAsync(address, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"{address} returned {(int)response.StatusCode} {response.ReasonPhrase}.", null, response.StatusCode);

        var total = response.Content.Headers.ContentLength;
        var partial = path + ".part";
        try
        {
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
        }
        finally
        {
            if (File.Exists(partial))
                File.Delete(partial);
        }
    }

    private ImportInfo ReadImports()
    {
        var file = Path.Combine(Folder, StatusFileName);
        if (!File.Exists(file))
            return new ImportInfo(null, null, null, null);
        try
        {
            return JsonSerializer.Deserialize<ImportInfo>(File.ReadAllText(file)) ?? new ImportInfo(null, null, null, null);
        }
        catch (JsonException)
        {
            return new ImportInfo(null, null, null, null);
        }
    }

    private void WriteImports(ImportInfo info)
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(Path.Combine(Folder, StatusFileName), JsonSerializer.Serialize(info));
    }

    private sealed record ImportInfo(string? ReaclibFile, DateTime? ReaclibImported, string? MassTableFile, int? MassTableEntries);
}
