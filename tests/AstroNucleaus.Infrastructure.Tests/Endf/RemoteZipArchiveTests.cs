using System.Net;
using System.Text;
using AstroNucleaus.Infrastructure.Endf.Download;

namespace AstroNucleaus.Infrastructure.Tests.Endf;

public class RemoteZipArchiveTests
{
    private static readonly Uri ArchiveUri = new("https://example.org/lib_neutrons.zip");

    [Fact]
    public async Task GetEntriesAsync_ReadsCentralDirectory()
    {
        var zip = RangeServer.CreateZip(
            ("lib_neutrons/README.txt", "readme"),
            ("lib_neutrons/n-001_H_001.endf", EndfTestTape.CreateSample()),
            ("lib_neutrons/n-026_Fe_056.endf", new string('x', 5000)));
        var archive = new RemoteZipArchive(new HttpClient(new RangeServer(zip)));

        var entries = await archive.GetEntriesAsync(ArchiveUri);

        Assert.Equal(
            ["lib_neutrons/README.txt", "lib_neutrons/n-001_H_001.endf", "lib_neutrons/n-026_Fe_056.endf"],
            entries.Select(e => e.Name));
        Assert.Equal(5000, entries[2].UncompressedSize);
        Assert.Equal(8, entries[2].Method);
        Assert.True(entries[2].CompressedSize < 5000);
    }

    [Fact]
    public async Task ExtractAsync_DownloadsOnlyTheRequestedEntry()
    {
        var large = RandomText(2_000_000);
        var zip = RangeServer.CreateZip(
            ("big.endf", large),
            ("small.endf", EndfTestTape.CreateSample()));
        var server = new RangeServer(zip);
        var archive = new RemoteZipArchive(new HttpClient(server));

        var entries = await archive.GetEntriesAsync(ArchiveUri);
        using var output = new MemoryStream();
        await archive.ExtractAsync(ArchiveUri, entries.Single(e => e.Name == "small.endf"), output);

        Assert.Equal(EndfTestTape.CreateSample(), Encoding.ASCII.GetString(output.ToArray()));
        Assert.True(server.BytesServed < zip.Length / 5, $"Served {server.BytesServed} of {zip.Length} bytes.");
    }

    [Fact]
    public async Task GetEntriesAsync_WorksWhenServerReturnsWholeSmallFile()
    {
        var zip = RangeServer.CreateZip(("a.endf", "content"));
        var handler = new WholeFileServer(zip);
        var archive = new RemoteZipArchive(new HttpClient(handler));

        var entries = await archive.GetEntriesAsync(ArchiveUri);

        Assert.Equal("a.endf", Assert.Single(entries).Name);
    }

    [Fact]
    public async Task GetEntriesAsync_RejectsNonZipContent()
    {
        var archive = new RemoteZipArchive(new HttpClient(new RangeServer(Encoding.ASCII.GetBytes(new string('a', 1000)))));

        await Assert.ThrowsAsync<InvalidDataException>(() => archive.GetEntriesAsync(ArchiveUri));
    }

    [Fact]
    public void ParseCentralDirectory_ReadsZip64ExtraField()
    {
        // Central directory entry whose sizes and offset are stored in the Zip64 extra field.
        var name = Encoding.ASCII.GetBytes("huge.endf");
        using var buffer = new MemoryStream();
        using (var writer = new BinaryWriter(buffer))
        {
            writer.Write(0x02014b50u);            // signature
            writer.Write((ushort)45);             // version made by
            writer.Write((ushort)45);             // version needed
            writer.Write((ushort)0);              // flags
            writer.Write((ushort)8);              // method
            writer.Write(0u);                     // time + date
            writer.Write(0u);                     // crc
            writer.Write(uint.MaxValue);          // compressed size -> zip64
            writer.Write(uint.MaxValue);          // uncompressed size -> zip64
            writer.Write((ushort)name.Length);
            writer.Write((ushort)28);             // extra length
            writer.Write((ushort)0);              // comment length
            writer.Write((ushort)0);              // disk
            writer.Write((ushort)0);              // internal attributes
            writer.Write(0u);                     // external attributes
            writer.Write(uint.MaxValue);          // local header offset -> zip64
            writer.Write(name);
            writer.Write((ushort)0x0001);
            writer.Write((ushort)24);
            writer.Write(6_000_000_000L);         // uncompressed
            writer.Write(5_000_000_000L);         // compressed
            writer.Write(4_500_000_000L);         // offset
        }

        var entry = Assert.Single(RemoteZipArchive.ParseCentralDirectory(buffer.ToArray(), 1));

        Assert.Equal("huge.endf", entry.Name);
        Assert.Equal(6_000_000_000L, entry.UncompressedSize);
        Assert.Equal(5_000_000_000L, entry.CompressedSize);
        Assert.Equal(4_500_000_000L, entry.LocalHeaderOffset);
    }

    private static string RandomText(int length)
    {
        var random = new Random(42);
        var chars = new char[length];
        for (var i = 0; i < length; i++)
            chars[i] = (char)('A' + random.Next(26));
        return new string(chars);
    }

    private sealed class WholeFileServer(byte[] content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(content) });
    }
}
