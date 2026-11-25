using System.Net;
using AstroNucleaus.Infrastructure.Endf.Download;

namespace AstroNucleaus.Infrastructure.Tests.Endf;

public class IaeaEndfLibraryCatalogTests
{
    private const string RootIndex = """
        <html><body><table>
        <tr><td><a href="/public/">Parent Directory</a></td></tr>
        <tr><td><a href="ENDF-B-VIII.1/">ENDF-B-VIII.1/</a></td></tr>
        <tr><td><a href="JEFF-4.0/">JEFF-4.0/</a></td></tr>
        <tr><td><a href="contents.htm">contents.htm</a></td></tr>
        <tr><td><a href="?C=M;O=A">Last modified</a></td></tr>
        <tr><td><a href="https://www.iaea.org/">IAEA.org</a></td></tr>
        </table></body></html>
        """;

    private const string FileIndex = """
        <a href="/public/download-endf/ENDF-B-VIII.1/">Parent Directory</a>
        <a href="n_001-H-1_0125.zip">n_001-H-1_0125.zip</a>
        <a href="n_013-Al-26M_1323.zip">n_013-Al-26M_1323.zip</a>
        <a href="readme.txt">readme.txt</a>
        """;

    [Fact]
    public void ParseDirectoryNames_ReturnsOnlyRelativeSubdirectories()
    {
        Assert.Equal(["ENDF-B-VIII.1", "JEFF-4.0"], IaeaEndfLibraryCatalog.ParseDirectoryNames(RootIndex));
    }

    [Fact]
    public void ParseFileNames_ReturnsZipFiles()
    {
        Assert.Equal(
            ["n_001-H-1_0125.zip", "n_013-Al-26M_1323.zip"],
            IaeaEndfLibraryCatalog.ParseFileNames(FileIndex, ".zip"));
    }

    [Fact]
    public async Task GetFilesAsync_BuildsAbsoluteUrisAndNuclideInfo()
    {
        var handler = new StubHandler(request =>
        {
            Assert.Equal("https://example.org/endf/ENDF-B-VIII.1/n/", request.RequestUri!.AbsoluteUri);
            return FileIndex;
        });
        var catalog = new IaeaEndfLibraryCatalog(new HttpClient(handler) { BaseAddress = new Uri("https://example.org/endf/") });

        var files = await catalog.GetFilesAsync("ENDF-B-VIII.1", "n");

        Assert.Equal(2, files.Count);
        var h1 = files[0];
        Assert.Equal(new Uri("https://example.org/endf/ENDF-B-VIII.1/n/n_001-H-1_0125.zip"), h1.Uri);
        Assert.Equal("n", h1.Projectile);
        Assert.Equal(1, h1.Z);
        Assert.Equal("H", h1.Symbol);
        Assert.Equal("1", h1.MassNumber);
        Assert.Null(h1.Isomer);
        Assert.Equal(125, h1.Mat);
        Assert.Equal("H-1", h1.DisplayName);

        var al26m = files[1];
        Assert.Equal("26", al26m.MassNumber);
        Assert.Equal("M", al26m.Isomer);
        Assert.Equal("Al-26M", al26m.DisplayName);
    }

    [Fact]
    public async Task GetLibrariesAsync_ReportsHttpErrors()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        var catalog = new IaeaEndfLibraryCatalog(new HttpClient(handler) { BaseAddress = new Uri("https://example.org/") });

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => catalog.GetLibrariesAsync());

        Assert.Equal(HttpStatusCode.Forbidden, ex.StatusCode);
    }

    [Theory]
    [InlineData("n_026-Fe-56_2631.zip", 26, "Fe", "56", null, 2631)]
    [InlineData("n_006-C-0_0600.zip", 6, "C", "0", null, 600)]
    [InlineData("p_092-U-235_9228.zip", 92, "U", "235", null, 9228)]
    [InlineData("n_095-Am-242M1_9547.zip", 95, "Am", "242", "M1", 9547)]
    public void EndfFileName_ParsesIaeaNames(string name, int z, string symbol, string a, string? isomer, int mat)
    {
        var file = EndfFileName.Parse("L", "S", name, new Uri("https://example.org/" + name));

        Assert.Equal(z, file.Z);
        Assert.Equal(symbol, file.Symbol);
        Assert.Equal(a, file.MassNumber);
        Assert.Equal(isomer, file.Isomer);
        Assert.Equal(mat, file.Mat);
    }

    [Fact]
    public void EndfFileName_KeepsUnknownNames()
    {
        var file = EndfFileName.Parse("L", "S", "something-else.zip", new Uri("https://example.org/x.zip"));

        Assert.Null(file.Z);
        Assert.Equal("something-else.zip", file.DisplayName);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public StubHandler(Func<HttpRequestMessage, string> html)
            : this(request => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(html(request)) })
        {
        }

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        {
            _respond = respond;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(_respond(request));
    }
}
