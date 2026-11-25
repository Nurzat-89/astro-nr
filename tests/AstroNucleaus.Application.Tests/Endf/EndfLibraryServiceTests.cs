using AstroNucleaus.Application.Endf;
using AstroNucleaus.Domain.Endf;

namespace AstroNucleaus.Application.Tests.Endf;

public class EndfLibraryServiceTests
{
    [Fact]
    public async Task DownloadAndParseAsync_ParsesTheDownloadedFile()
    {
        var file = new EndfRemoteFile("ENDF-B-VIII.1", "n", "n_001-H-1_0125.zip", new Uri("https://example.org/x.zip"));
        var tape = new EndfTape("id", 1, [], []);
        var downloader = new FakeDownloader("C:/cache/h1.endf");
        var parser = new FakeParser(tape);
        var service = new EndfLibraryService([new FakeCatalog("a", ["A-lib"])], downloader, parser);

        var result = await service.DownloadAndParseAsync(file);

        Assert.Same(tape, result);
        Assert.Same(file, downloader.Requested);
        Assert.Equal("C:/cache/h1.endf", parser.ParsedPath);
    }

    [Fact]
    public async Task DownloadAndParseAsync_DoesNotParseWhenDownloadFails()
    {
        var parser = new FakeParser(new EndfTape("id", 1, [], []));
        var service = new EndfLibraryService([new FakeCatalog("a", [])], new FakeDownloader(null), parser);
        var file = new EndfRemoteFile("L", "n", "f.zip", new Uri("https://example.org/f.zip"));

        await Assert.ThrowsAsync<HttpRequestException>(() => service.DownloadAndParseAsync(file));

        Assert.Null(parser.ParsedPath);
    }

    [Fact]
    public async Task RoutesCallsToTheSelectedSource()
    {
        var service = new EndfLibraryService(
            [new FakeCatalog("nndc", ["ENDF-B-VIII.0"]), new FakeCatalog("iaea", ["JEFF-4.0"])],
            new FakeDownloader(null),
            new FakeParser(new EndfTape("id", 1, [], [])));

        Assert.Equal(["nndc", "iaea"], service.Sources.Select(s => s.Id));
        Assert.Equal(["JEFF-4.0"], await service.GetLibrariesAsync("iaea"));
        Assert.Equal(["ENDF-B-VIII.0"], await service.GetLibrariesAsync("nndc"));
        await Assert.ThrowsAsync<ArgumentException>(() => service.GetLibrariesAsync("unknown"));
    }

    private sealed class FakeCatalog(string id, IReadOnlyList<string> libraries) : IEndfLibraryCatalog
    {
        public EndfSourceInfo Source { get; } = new(id, id.ToUpperInvariant());

        public Task<IReadOnlyList<string>> GetLibrariesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(libraries);

        public Task<IReadOnlyList<string>> GetSublibrariesAsync(string library, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>([]);

        public Task<IReadOnlyList<EndfRemoteFile>> GetFilesAsync(string library, string sublibrary, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<EndfRemoteFile>>([]);
    }

    private sealed class FakeDownloader(string? path) : IEndfFileDownloader
    {
        public EndfRemoteFile? Requested { get; private set; }

        public Task<string> DownloadAsync(EndfRemoteFile file, IProgress<DownloadProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            Requested = file;
            return path is null
                ? Task.FromException<string>(new HttpRequestException("offline"))
                : Task.FromResult(path);
        }
    }

    private sealed class FakeParser(EndfTape tape) : IEndfParser
    {
        public string? ParsedPath { get; private set; }

        public EndfTape Parse(TextReader reader) => tape;

        public EndfTape ParseFile(string path)
        {
            ParsedPath = path;
            return tape;
        }
    }
}
