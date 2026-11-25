using System.IO.Compression;
using System.Text;
using AstroNucleaus.Domain.SProcess;
using AstroNucleaus.Infrastructure.Endf;
using AstroNucleaus.Infrastructure.Endf.Parsing;
using AstroNucleaus.Infrastructure.SProcess;
using AstroNucleaus.Infrastructure.Tests.Endf;
using Microsoft.Extensions.Options;

namespace AstroNucleaus.Infrastructure.Tests.SProcess;

public sealed class SProcessDataTests : IDisposable
{
    private const string EndfMacs30 = """
        60, ENDF/B-VII.1, 102, 2631, 26-Fe- 56, 30160.55 eV, 1.15049E-2, 2.1658E-3, 1.679E6, 3.161E5
        93, ENDF/B-VII.1, 102, 3440, 34-Se- 79, 30160.55 eV, 4.13114E-1, 7.33662E-2, 6.014E7, 1.068E7
        300, ENDF/B-VII.1, 102, 9547, 95-Am-242m, 30160.55 eV, 1.0E0, 0E0, 0E0, 0E0
        """;

    private const string EafMacs30 = """
        1, EAF 2010, 102, 125, H-1,30160.55 eV, 1.51749E-4, , 3.105E4,
        70, EAF 2010, 102, 2631, Fe-56,30160.55 eV, 9.9E-3, , 1.0E6,
        80, EAF 2010, 102, 2834, Ni-63,30160.55 eV, 3.1E-2, , 4.5E6,
        """;

    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"astronucleaus-sproc-{Guid.NewGuid():N}");

    public SProcessDataTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public void MacsParser_ReadsBothFileFormatsAndSkipsIsomers()
    {
        var rows = MacsTableParser.Parse(new StringReader(EndfMacs30 + "\n" + EafMacs30)).ToList();

        Assert.Equal(
            [new Nuclide(26, 56), new Nuclide(34, 79), new Nuclide(1, 1), new Nuclide(26, 56), new Nuclide(28, 63)],
            rows.Select(r => r.Nuclide));
        Assert.All(rows, r => Assert.Equal(30.16055, r.KTkeV, 9));
        Assert.Equal(0.413114, rows[1].Barns, 9);
    }

    [Fact]
    public void Parser_ReadsDecayDataFromFile8()
    {
        var tape = new EndfParser().Parse(new StringReader(DecayFile(36085, 3.39e8, rtyp: 1.0, stable: false)));

        var decay = Assert.Single(tape.Materials).Decay;
        Assert.NotNull(decay);
        Assert.False(decay.IsStable);
        Assert.Equal(3.39e8, decay.HalfLifeSeconds);
        var branch = Assert.Single(decay.Branches);
        Assert.Equal("β-", branch.Mode);
        Assert.Equal(1, branch.DeltaZ);
        Assert.Equal(1.0, branch.Fraction);
    }

    [Fact]
    public void Parser_ReadsStableNuclideFromFile8()
    {
        var tape = new EndfParser().Parse(new StringReader(DecayFile(26056, 0, rtyp: 0, stable: true)));

        Assert.True(Assert.Single(tape.Materials).Decay!.IsStable);
    }

    [Fact]
    public void DataSource_MergesMacsLibrariesAndReadsDecayArchive()
    {
        var macsArchive = Path.Combine(_folder, "macs.zip");
        File.WriteAllBytes(macsArchive, BinaryZip(
            ("ReadMe.txt", Encoding.ASCII.GetBytes("readme")),
            ("ENDFB71.zip", InnerZip("ENDF71-Max-MT102-3.016055E+04.txt", EndfMacs30)),
            ("EAF2010.zip", InnerZip("EAF2010-Max-MT102-3.016055E+04.txt", EafMacs30))));

        var decayArchive = Path.Combine(_folder, "decay.zip");
        File.WriteAllBytes(decayArchive, RangeServer.CreateZip(
            ("ENDF-B-VIII.0_decay/dec-036_Kr_085.endf", DecayFile(36085, 3.39e8, 1.0, stable: false)),
            ("ENDF-B-VIII.0_decay/dec-026_Fe_056.endf", DecayFile(26056, 0, 0, stable: true)),
            ("ENDF-B-VIII.0_decay/dec-036_Kr_085m1.endf", DecayFile(36085, 1.6e4, 3.0, stable: false)),
            ("ENDF-B-VIII.0_decay/dec-055_Cs_137.endf", DecayFile(55137, 9.5e8, 1.0, stable: false))));

        var source = new NndcSProcessDataSource(new HttpClient(), Options.Create(new EndfOptions { CacheDirectory = _folder }));
        var data = source.Build(macsArchive, decayArchive, maxZ: 42);

        // ENDF/B-VII.1 wins over EAF-2010; EAF fills nuclides missing in ENDF.
        Assert.Equal(0.0115049, data.CaptureBarns(new Nuclide(26, 56), 30.16055), 9);
        Assert.Equal(0.031, data.CaptureBarns(new Nuclide(28, 63), 30.16055), 9);
        Assert.Contains(new Nuclide(1, 1), data.CaptureCrossSections.Keys);

        // Ground states inside Z 26..42 only.
        Assert.Equal([new Nuclide(26, 56), new Nuclide(36, 85)], data.Decays.Keys.OrderBy(n => n));
        Assert.Equal(3.39e8, data.Decays[new Nuclide(36, 85)].HalfLifeSeconds);
        Assert.Contains("EAF-2010", data.Description);
    }

    private static byte[] InnerZip(string name, string content) => RangeServer.CreateZip((name, content));

    private static byte[] BinaryZip(params (string Name, byte[] Content)[] entries)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                using var stream = archive.CreateEntry(name).Open();
                stream.Write(content);
            }
        }

        return buffer.ToArray();
    }

    /// <summary>Minimal ENDF-6 decay file with MF=1/MT=451 omitted and MF=8/MT=457 only.</summary>
    private static string DecayFile(int za, double halfLife, double rtyp, bool stable)
    {
        const int mat = 3646;
        var tape = new EndfTestTape();
        tape.Line(" decay test", 1, 0, 0);
        tape.Cont(za, za % 1000 - 0.9, 0, 0, stable ? 1 : 0, 0, mat, 8, 457);
        tape.Cont(halfLife, halfLife * 1e-3, 0, 0, 3, 0, mat, 8, 457);
        tape.Line(EndfTestTape.R(1.0) + EndfTestTape.R(0) + EndfTestTape.R(2.0), mat, 8, 457);
        tape.Cont(4.5, 1, 0, 0, 6, 1, mat, 8, 457);
        tape.Line(EndfTestTape.R(rtyp) + EndfTestTape.R(0) + EndfTestTape.R(6.87e5) + EndfTestTape.R(2e3)
                  + EndfTestTape.R(stable ? 0 : 1.0) + EndfTestTape.R(0), mat, 8, 457);
        tape.Send(mat, 8);
        tape.Fend(mat);
        tape.Mend();
        tape.Tend();
        return tape.ToString();
    }
}
