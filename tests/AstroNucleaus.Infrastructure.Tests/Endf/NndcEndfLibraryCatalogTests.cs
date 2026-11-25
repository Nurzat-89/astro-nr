using AstroNucleaus.Infrastructure.Endf;
using AstroNucleaus.Infrastructure.Endf.Download;
using Microsoft.Extensions.Options;

namespace AstroNucleaus.Infrastructure.Tests.Endf;

public class NndcEndfLibraryCatalogTests
{
    private static NndcEndfLibraryCatalog CreateCatalog(HttpMessageHandler handler) =>
        new(new HttpClient(handler), Options.Create(new EndfOptions
        {
            NndcLibraries =
            [
                new NndcLibraryOptions
                {
                    Name = "ENDF-B-VIII.0",
                    ArchiveUrlTemplate = "https://example.org/zips/ENDF-B-VIII.0_{sublibrary}.zip",
                    Sublibraries = ["neutrons", "protons"],
                },
            ],
        }));

    [Fact]
    public async Task ListsConfiguredLibrariesAndSublibraries()
    {
        var catalog = CreateCatalog(new RangeServer([]));

        Assert.Equal("nndc", catalog.Source.Id);
        Assert.Equal(["ENDF-B-VIII.0"], await catalog.GetLibrariesAsync());
        Assert.Equal(["neutrons", "protons"], await catalog.GetSublibrariesAsync("ENDF-B-VIII.0"));
        await Assert.ThrowsAsync<ArgumentException>(() => catalog.GetSublibrariesAsync("JEFF"));
    }

    [Fact]
    public async Task GetFilesAsync_ListsEvaluationsInsideTheArchiveSortedByNuclide()
    {
        var zip = RangeServer.CreateZip(
            ("ENDF-B-VIII.0_neutrons/CHANGELOG.txt", "log"),
            ("ENDF-B-VIII.0_neutrons/README.txt", "readme"),
            ("ENDF-B-VIII.0_neutrons/n-026_Fe_056.endf", "fe56"),
            ("ENDF-B-VIII.0_neutrons/n-013_Al_026m1.endf", "al26m"),
            ("ENDF-B-VIII.0_neutrons/n-001_H_002.endf", "h2"),
            ("ENDF-B-VIII.0_neutrons/n-001_H_001.endf", "h1"));
        var catalog = CreateCatalog(new RangeServer(zip));

        var files = await catalog.GetFilesAsync("ENDF-B-VIII.0", "neutrons");

        Assert.Equal(["H-1", "H-2", "Al-26m1", "Fe-56"], files.Select(f => f.DisplayName));

        var fe56 = files[^1];
        Assert.Equal("nndc", fe56.SourceId);
        Assert.Equal("n-026_Fe_056.endf", fe56.FileName);
        Assert.Equal("ENDF-B-VIII.0_neutrons/n-026_Fe_056.endf", fe56.ArchiveEntry);
        Assert.Equal(new Uri("https://example.org/zips/ENDF-B-VIII.0_neutrons.zip"), fe56.Uri);
        Assert.Equal(26, fe56.Z);
        Assert.Equal("56", fe56.MassNumber);
        Assert.Equal(4, fe56.Size);
        Assert.Null(fe56.Mat);
    }

    [Theory]
    [InlineData("n-000_n_001.endf", "n", 0, "n", "1", null)]
    [InlineData("p-092_U_235.endf", "p", 92, "U", "235", null)]
    [InlineData("n-047_Ag_110m1.endf", "n", 47, "Ag", "110", "m1")]
    [InlineData("n-006_C_000.endf", "n", 6, "C", "0", null)]
    public void EndfFileName_ParsesNndcEntries(string name, string projectile, int z, string symbol, string a, string? isomer)
    {
        var file = EndfFileName.ParseArchiveEntry("L", "S", new Uri("https://example.org/x.zip"), "dir/" + name);

        Assert.Equal(name, file.FileName);
        Assert.Equal(projectile, file.Projectile);
        Assert.Equal(z, file.Z);
        Assert.Equal(symbol, file.Symbol);
        Assert.Equal(a, file.MassNumber);
        Assert.Equal(isomer, file.Isomer);
    }

    [Fact]
    public void EndfFileName_KeepsUnknownEntryNames()
    {
        var file = EndfFileName.ParseArchiveEntry("L", "S", new Uri("https://example.org/x.zip"), "dir/tsl-HinH2O.endf");

        Assert.Null(file.Z);
        Assert.Equal("tsl-HinH2O.endf", file.DisplayName);
    }
}
