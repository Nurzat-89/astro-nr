using System.Globalization;
using System.Text;
using AstroNucleaus.Domain.SProcess;
using AstroNucleaus.Infrastructure.Endf;
using AstroNucleaus.Infrastructure.RProcess;
using Microsoft.Extensions.Options;

namespace AstroNucleaus.Infrastructure.Tests.RProcess;

public sealed class RProcessDataTests : IDisposable
{
    // Genuine lines of AME2020 mass_1.mas20 (with a header line that must be ignored).
    private const string Ame = """
        1N-Z    N    Z   A  EL    O     MASS EXCESS           BINDING ENERGY/A        BETA-DECAY ENERGY
        0  1    1    0    1  n         8071.31806     0.00044       0.0        0.0     B-    782.3470     0.0004    1 008664.91590
           4   30   26   56 Fe       -60607.163       0.268      8790.3563     0.0048  B-  -4566.6455     0.4104   55 934935.537
          -2   46   48   94 Cd    x  -40440#        500#         8102#         5#      B-      *                   93 956586#
        """;

    // Genuine lines of NUBASE2020 nubase_4.mas20, plus an isomer line (i = 1) that must be skipped.
    private const string Nubase = """
        # Nubase2020 header
        056 0260   56Fe   -60607.16       0.27                                stbl              0+            11
        057 0260   57Fe   -60182.02       0.27                                stbl              1/2-*         98
        057 0261   57Fe   -60168.0        0.3                                 98      ns 1      3/2-          98
        132 0500   132Sn  -76546.6        2.0                                  39.7    s 0.8    0+            18
        """;

    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"astronucleaus-r-{Guid.NewGuid():N}");

    public RProcessDataTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public void Ame2020_ReadsMassExcessIncludingEstimatedValues()
    {
        var masses = MassTableParsers.ParseAme2020(new StringReader(Ame));

        Assert.Equal(3, masses.Count);
        Assert.Equal(8.07131806, masses[new Nuclide(0, 1)], 9);
        Assert.Equal(-60.607163, masses[new Nuclide(26, 56)], 9);
        Assert.Equal(-40.440, masses[new Nuclide(48, 94)], 9); // "-40440#" is an estimate
    }

    [Fact]
    public void Nubase_ReadsGroundStateSpins()
    {
        var spins = MassTableParsers.ParseNubaseSpins(new StringReader(Nubase.Replace("\r", string.Empty)));

        Assert.Equal(0.0, spins[new Nuclide(26, 56)]);
        Assert.Equal(0.5, spins[new Nuclide(26, 57)]); // ground state, not the 3/2- isomer
        Assert.Equal(0.0, spins[new Nuclide(50, 132)]);
    }

    [Fact]
    public void UserTable_ReadsZAMassExcessAndRejectsBrokenLines()
    {
        var table = MassTableParsers.ParseUserTable(new StringReader("""
            # FRDM2012 excerpt
            Z  A  ME
            46 128 -51.2
            46,129,-46.0
            """));

        Assert.Equal(-51.2, table[new Nuclide(46, 128)]);
        Assert.Equal(-46.0, table[new Nuclide(46, 129)]);
        Assert.Throws<FormatException>(() => MassTableParsers.ParseUserTable(new StringReader("46 128 -51.2\nbroken line")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Reaclib_ParsesBothLayoutsAndSumsSets(bool reaclib2)
    {
        var path = Path.Combine(_folder, "reaclib.dat");
        File.WriteAllText(path, SampleReaclib(reaclib2));

        var (captures, reverse, decays) = LocalRProcessDataSource.ReadReaclib(path);

        // Two (n,γ) sets on ⁵⁶Fe are summed: 1e6 + 2e6.
        // REACLIB stores 7 significant digits of ln(rate): compare with a relative tolerance.
        Assert.Equal(3e6, captures[new Nuclide(26, 56)].Evaluate(1.0), 3e6 * 1e-5);
        Assert.Equal(5e9, reverse[new Nuclide(26, 57)].Evaluate(1.0), 5e9 * 1e-5);

        var channels = decays[new Nuclide(26, 57)];
        Assert.Contains(channels, c => c.Daughter == new Nuclide(27, 57) && c.DelayedNeutrons == 0 && Math.Abs(c.RatePerSecond - 0.5) < 1e-6);
        Assert.Contains(channels, c => c.Daughter == new Nuclide(27, 56) && c.DelayedNeutrons == 1 && Math.Abs(c.RatePerSecond - 0.1) < 1e-6);

        // The isomer (al-6) and the charged-particle reaction are ignored.
        Assert.DoesNotContain(new Nuclide(13, 26), captures.Keys);
        Assert.Single(captures);
    }

    [Fact]
    public void Build_PrefersAmeMassesAndFillsGapsFromTheTheoryTable()
    {
        var reaclib = Write("reaclib.dat", SampleReaclib(reaclib2: true));
        var ame = Write("ame.txt", Ame);
        var nubase = Write("nubase.txt", Nubase);
        var theory = Write("theory.txt", "26 56 -99.0\n26 57 -60.18\n48 130 -61.1\n");

        var data = LocalRProcessDataSource.Build(reaclib, ame, nubase, theory);

        Assert.Equal(-60.607163, data.MassExcessMeV[new Nuclide(26, 56)], 9); // AME wins
        Assert.Equal(-60.18, data.MassExcessMeV[new Nuclide(26, 57)], 9);     // theory fills the gap
        Assert.Equal(-61.1, data.MassExcessMeV[new Nuclide(48, 130)], 9);
        Assert.Equal(2.0, data.StatisticalWeight(new Nuclide(26, 57)));       // J = 1/2
        Assert.Equal(-60.607163 + 8.07131806 - -60.18, data.NeutronSeparationEnergy(new Nuclide(26, 57))!.Value, 9);
        Assert.Contains("theoretical table", data.Description);
    }

    [Fact]
    public async Task Import_ValidatesFilesAndReportsStatus()
    {
        var source = new LocalRProcessDataSource(new HttpClient(), Options.Create(new EndfOptions { CacheDirectory = _folder }));
        Assert.False(source.GetStatus().HasReaclib);

        await Assert.ThrowsAsync<FormatException>(() => source.ImportReaclibAsync(Write("bad.dat", "this is not reaclib\n")));

        await source.ImportReaclibAsync(Write("my-reaclib.txt", SampleReaclib(reaclib2: false)));
        await source.ImportMassTableAsync(Write("frdm.txt", "48 130 -61.1\n"));
        var status = source.GetStatus();

        Assert.Equal("my-reaclib.txt", status.ReaclibFile);
        Assert.Equal("frdm.txt", status.MassTableFile);
        Assert.Equal(1, status.MassTableEntries);

        source.RemoveMassTable();
        Assert.Null(source.GetStatus().MassTableFile);
    }

    [Fact]
    public async Task DownloadReaclib_StoresAValidSnapshot()
    {
        var source = SourceWith(_ => new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(SampleReaclib(reaclib2: true), Encoding.Latin1),
        });

        await source.DownloadReaclibAsync();

        var status = source.GetStatus();
        Assert.True(status.HasReaclib);
        Assert.Equal("reaclib_default2_20250330 (pynucastro)", status.ReaclibFile);
        Assert.Empty(Directory.GetFiles(Path.Combine(_folder, "rprocess"), "*.tmp*"));
    }

    [Fact]
    public async Task DownloadReaclib_KeepsNothingOnErrorsOrInvalidFiles()
    {
        var notFound = SourceWith(_ => new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
        await Assert.ThrowsAsync<HttpRequestException>(() => notFound.DownloadReaclibAsync());
        Assert.False(notFound.GetStatus().HasReaclib);

        var html = SourceWith(_ => new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("<html>blocked</html>") });
        await Assert.ThrowsAnyAsync<Exception>(() => html.DownloadReaclibAsync());
        Assert.False(html.GetStatus().HasReaclib);
        Assert.DoesNotContain(Directory.GetFiles(Path.Combine(_folder, "rprocess")), f => !f.EndsWith("imports.json", StringComparison.Ordinal));
    }

    private LocalRProcessDataSource SourceWith(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        new(new HttpClient(new StubHandler(respond)), Options.Create(new EndfOptions { CacheDirectory = _folder }));

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }

    private string Write(string name, string content)
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllText(path, content.Replace("\r", string.Empty), Encoding.Latin1);
        return path;
    }

    /// <summary>REACLIB text in the exact column layout (REACLIB 1 or REACLIB 2).</summary>
    private static string SampleReaclib(bool reaclib2)
    {
        var sets = new List<(int Chapter, string[] Names, string Label, char Res, char Rev, double Rate)>
        {
            (1, ["fe57", "co57"], "bet-", 'w', ' ', 0.5),
            (2, ["fe57", "n", "co56"], "bet-", 'w', ' ', 0.1),
            (2, ["fe57", "n", "fe56"], "ths8", 'n', 'v', 5e9),
            (4, ["n", "fe56", "fe57"], "ths8", 'n', ' ', 1e6),
            (4, ["n", "fe56", "fe57"], "ths8", 'r', ' ', 2e6),
            (4, ["n", "al-6", "al27"], "ths8", 'n', ' ', 1e6),
            (5, ["p", "fe56", "n", "co56"], "ths8", 'n', ' ', 1.0),
        };

        var text = new StringBuilder();
        var chapter = 0;
        foreach (var (ch, names, label, res, rev, rate) in sets)
        {
            if (reaclib2 || ch != chapter)
            {
                text.Append(ch.ToString(CultureInfo.InvariantCulture)).Append('\n');
                if (!reaclib2)
                    text.Append("\n\n");
                chapter = ch;
            }

            var header = new StringBuilder("     ");
            foreach (var name in names)
                header.Append(name.PadLeft(5));
            header.Append(new string(' ', 5 * (6 - names.Length)));
            header.Append(new string(' ', 8)).Append(label.PadRight(4)).Append(res).Append(rev).Append("   ")
                .Append(1.234e0.ToString("0.00000e+00", CultureInfo.InvariantCulture).PadLeft(12));
            text.Append(header).Append('\n');
            text.Append(E(Math.Log(rate))).Append(E(0)).Append(E(0)).Append(E(0)).Append('\n');
            text.Append(E(0)).Append(E(0)).Append(E(0)).Append('\n');
        }

        return text.ToString();

        static string E(double v) => v.ToString("0.000000e+00", CultureInfo.InvariantCulture).PadLeft(13);
    }
}
