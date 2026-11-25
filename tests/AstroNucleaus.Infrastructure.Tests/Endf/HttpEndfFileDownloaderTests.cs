using System.IO.Compression;
using System.Net;
using System.Text;
using AstroNucleaus.Application.Endf;
using AstroNucleaus.Infrastructure.Endf;
using AstroNucleaus.Infrastructure.Endf.Download;
using Microsoft.Extensions.Options;

namespace AstroNucleaus.Infrastructure.Tests.Endf;

public sealed class HttpEndfFileDownloaderTests : IDisposable
{
    private readonly string _cache = Path.Combine(Path.GetTempPath(), $"astronucleaus-cache-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_cache))
            Directory.Delete(_cache, recursive: true);
    }

    [Fact]
    public async Task DownloadAsync_UnzipsIntoCacheAndReusesIt()
    {
        var zip = CreateZip("n-001_H_001.endf", EndfTestTape.CreateSample());
        var requests = 0;
        var downloader = CreateDownloader(_ =>
        {
            requests++;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(zip) };
        });
        var file = new EndfRemoteFile("ENDF-B-VIII.1", "n", "n_001-H-1_0125.zip", new Uri("https://example.org/n_001-H-1_0125.zip"))
        {
            SourceId = "iaea",
        };
        var progress = new List<DownloadProgress>();

        var first = await downloader.DownloadAsync(file, new SyncProgress(progress.Add));
        var second = await downloader.DownloadAsync(file);

        Assert.Equal(1, requests);
        Assert.Equal(first, second);
        Assert.Equal(Path.Combine(_cache, "iaea", "ENDF-B-VIII.1", "n", "n_001-H-1_0125", "n-001_H_001.endf"), first);
        Assert.Equal(EndfTestTape.CreateSample(), await File.ReadAllTextAsync(first));
        Assert.Equal(1.0, progress[^1].Fraction);
    }

    [Fact]
    public async Task DownloadAsync_ExtractsSingleEntryOfRemoteArchive()
    {
        var zip = RangeServer.CreateZip(
            ("ENDF-B-VIII.0_neutrons/n-001_H_001.endf", EndfTestTape.CreateSample()),
            ("ENDF-B-VIII.0_neutrons/n-026_Fe_056.endf", new string('x', 100_000)));
        var server = new RangeServer(zip);
        var downloader = new HttpEndfFileDownloader(
            new HttpClient(server), Options.Create(new EndfOptions { CacheDirectory = _cache }));
        var file = new EndfRemoteFile(
            "ENDF-B-VIII.0", "neutrons", "n-001_H_001.endf", new Uri("https://example.org/ENDF-B-VIII.0_neutrons.zip"))
        {
            SourceId = "nndc",
            ArchiveEntry = "ENDF-B-VIII.0_neutrons/n-001_H_001.endf",
        };

        var path = await downloader.DownloadAsync(file);
        var requestsAfterFirst = server.Requests.Count;
        var again = await downloader.DownloadAsync(file);

        Assert.Equal(Path.Combine(_cache, "nndc", "ENDF-B-VIII.0", "neutrons", "n-001_H_001", "n-001_H_001.endf"), path);
        Assert.Equal(path, again);
        Assert.Equal(EndfTestTape.CreateSample(), await File.ReadAllTextAsync(path));
        Assert.All(server.Requests, r => Assert.NotNull(r));
        Assert.Equal(requestsAfterFirst, server.Requests.Count);
    }

    [Fact]
    public async Task DownloadAsync_ReportsMissingArchiveEntry()
    {
        var zip = RangeServer.CreateZip(("other.endf", "x"));
        var downloader = new HttpEndfFileDownloader(
            new HttpClient(new RangeServer(zip)), Options.Create(new EndfOptions { CacheDirectory = _cache }));
        var file = new EndfRemoteFile("L", "neutrons", "missing.endf", new Uri("https://example.org/a.zip"))
        {
            SourceId = "nndc",
            ArchiveEntry = "missing.endf",
        };

        await Assert.ThrowsAsync<InvalidDataException>(() => downloader.DownloadAsync(file));

        Assert.False(Directory.Exists(Path.Combine(_cache, "nndc", "L", "neutrons", "missing")));
    }

    [Fact]
    public async Task DownloadAsync_LeavesNoCacheEntryOnHttpError()
    {
        var downloader = CreateDownloader(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var file = new EndfRemoteFile("L", "n", "missing.zip", new Uri("https://example.org/missing.zip"));

        await Assert.ThrowsAsync<HttpRequestException>(() => downloader.DownloadAsync(file));

        Assert.False(Directory.Exists(Path.Combine(_cache, "other", "L", "n", "missing")));
        Assert.False(Directory.Exists(Path.Combine(_cache, "other", "L", "n", "missing.download")));
    }

    [Fact]
    public async Task DownloadAsync_KeepsPlainTextFiles()
    {
        var downloader = CreateDownloader(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(EndfTestTape.CreateSample()),
        });
        var file = new EndfRemoteFile("L", "n", "plain.zip", new Uri("https://example.org/plain.zip"));

        var path = await downloader.DownloadAsync(file);

        Assert.Equal(".endf", Path.GetExtension(path));
        Assert.Equal(EndfTestTape.CreateSample(), await File.ReadAllTextAsync(path));
    }

    private HttpEndfFileDownloader CreateDownloader(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        new(new HttpClient(new StubHandler(respond)), Options.Create(new EndfOptions { CacheDirectory = _cache }));

    private static byte[] CreateZip(string entryName, string content)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var writer = new StreamWriter(archive.CreateEntry(entryName).Open(), Encoding.ASCII);
            writer.Write(content);
        }

        return buffer.ToArray();
    }

    private sealed class SyncProgress(Action<DownloadProgress> report) : IProgress<DownloadProgress>
    {
        public void Report(DownloadProgress value) => report(value);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
