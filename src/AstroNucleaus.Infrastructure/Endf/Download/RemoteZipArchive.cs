using System.Buffers.Binary;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using AstroNucleaus.Application.Endf;

namespace AstroNucleaus.Infrastructure.Endf.Download;

/// <summary>An entry of a remote zip archive, read from its central directory.</summary>
internal sealed record RemoteZipEntry(
    string Name,
    ushort Flags,
    ushort Method,
    long CompressedSize,
    long UncompressedSize,
    long LocalHeaderOffset)
{
    public bool IsDirectory => Name.EndsWith('/');
}

/// <summary>
/// Reads a zip archive on an HTTP server with range requests, without downloading the whole file:
/// the central directory is read from the end of the archive, and single entries are fetched and
/// decompressed on demand. Supports stored and deflated entries and Zip64 archives.
/// </summary>
internal sealed class RemoteZipArchive
{
    private const uint EndOfCentralDirectorySignature = 0x06054b50;
    private const uint Zip64LocatorSignature = 0x07064b50;
    private const uint Zip64EndOfCentralDirectorySignature = 0x06064b50;
    private const uint CentralDirectorySignature = 0x02014b50;
    private const uint LocalFileHeaderSignature = 0x04034b50;
    private const int EndOfCentralDirectorySize = 22;
    private const int Zip64LocatorSize = 20;
    private const int MaxCommentLength = ushort.MaxValue;
    private const int LocalFileHeaderSize = 30;
    private const int MaxCentralDirectorySize = 64 * 1024 * 1024;
    private const ushort Stored = 0;
    private const ushort Deflated = 8;

    private readonly HttpClient _http;

    public RemoteZipArchive(HttpClient http)
    {
        _http = http;
    }

    public async Task<IReadOnlyList<RemoteZipEntry>> GetEntriesAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        // One suffix request returns the end of the archive and its total length.
        var (tail, totalLength) = await ReadTailAsync(
            uri, EndOfCentralDirectorySize + MaxCommentLength + Zip64LocatorSize, cancellationToken).ConfigureAwait(false);
        var tailStart = totalLength - tail.Length;

        var eocd = FindSignatureBackwards(tail, EndOfCentralDirectorySignature, EndOfCentralDirectorySize)
                   ?? throw new InvalidDataException($"{uri} is not a zip archive.");

        long entryCount = ReadUInt16(tail, eocd + 10);
        long centralDirectorySize = ReadUInt32(tail, eocd + 12);
        long centralDirectoryOffset = ReadUInt32(tail, eocd + 16);

        if (entryCount == ushort.MaxValue || centralDirectorySize == uint.MaxValue || centralDirectoryOffset == uint.MaxValue)
        {
            var locator = eocd - Zip64LocatorSize;
            if (locator < 0 || ReadUInt32(tail, locator) != Zip64LocatorSignature)
                throw new InvalidDataException($"{uri}: Zip64 locator not found.");

            var zip64Offset = checked((long)ReadUInt64(tail, locator + 8));
            var zip64 = await ReadRangeAsync(uri, zip64Offset, 56, null, cancellationToken).ConfigureAwait(false);
            if (ReadUInt32(zip64, 0) != Zip64EndOfCentralDirectorySignature)
                throw new InvalidDataException($"{uri}: invalid Zip64 end of central directory.");

            entryCount = checked((long)ReadUInt64(zip64, 32));
            centralDirectorySize = checked((long)ReadUInt64(zip64, 40));
            centralDirectoryOffset = checked((long)ReadUInt64(zip64, 48));
        }

        if (centralDirectorySize > MaxCentralDirectorySize)
            throw new InvalidDataException($"{uri}: central directory of {centralDirectorySize} bytes is too large.");

        byte[] centralDirectory;
        if (centralDirectoryOffset >= tailStart && centralDirectoryOffset + centralDirectorySize <= totalLength)
        {
            var start = (int)(centralDirectoryOffset - tailStart);
            centralDirectory = tail.AsSpan(start, (int)centralDirectorySize).ToArray();
        }
        else
        {
            centralDirectory = await ReadRangeAsync(
                uri, centralDirectoryOffset, (int)centralDirectorySize, null, cancellationToken).ConfigureAwait(false);
        }

        return ParseCentralDirectory(centralDirectory, entryCount);
    }

    /// <summary>Downloads one entry and writes its decompressed content to <paramref name="destination"/>.</summary>
    public async Task ExtractAsync(
        Uri uri,
        RemoteZipEntry entry,
        Stream destination,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if ((entry.Flags & 1) != 0)
            throw new NotSupportedException($"{entry.Name} is encrypted.");
        if (entry.Method is not (Stored or Deflated))
            throw new NotSupportedException($"{entry.Name} uses unsupported compression method {entry.Method}.");

        var header = await ReadRangeAsync(uri, entry.LocalHeaderOffset, LocalFileHeaderSize, null, cancellationToken)
            .ConfigureAwait(false);
        if (ReadUInt32(header, 0) != LocalFileHeaderSignature)
            throw new InvalidDataException($"{uri}: invalid local header for {entry.Name}.");

        var dataOffset = entry.LocalHeaderOffset + LocalFileHeaderSize + ReadUInt16(header, 26) + ReadUInt16(header, 28);
        if (entry.CompressedSize == 0)
            return;
        if (entry.CompressedSize > int.MaxValue)
            throw new NotSupportedException($"{entry.Name} is larger than 2 GB.");

        var data = await ReadRangeAsync(uri, dataOffset, (int)entry.CompressedSize, progress, cancellationToken)
            .ConfigureAwait(false);

        using var compressed = new MemoryStream(data, writable: false);
        if (entry.Method == Deflated)
        {
            await using var inflater = new DeflateStream(compressed, CompressionMode.Decompress);
            await inflater.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await compressed.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
        }
    }

    internal static IReadOnlyList<RemoteZipEntry> ParseCentralDirectory(ReadOnlySpan<byte> data, long expectedCount)
    {
        var entries = new List<RemoteZipEntry>((int)Math.Min(expectedCount, 100_000));
        var position = 0;

        while (position + 46 <= data.Length && ReadUInt32(data, position) == CentralDirectorySignature)
        {
            var flags = ReadUInt16(data, position + 8);
            var method = ReadUInt16(data, position + 10);
            long compressedSize = ReadUInt32(data, position + 20);
            long uncompressedSize = ReadUInt32(data, position + 24);
            var nameLength = ReadUInt16(data, position + 28);
            var extraLength = ReadUInt16(data, position + 30);
            var commentLength = ReadUInt16(data, position + 32);
            long localHeaderOffset = ReadUInt32(data, position + 42);

            var end = position + 46 + nameLength + extraLength + commentLength;
            if (end > data.Length)
                throw new InvalidDataException("Truncated zip central directory.");

            var nameBytes = data.Slice(position + 46, nameLength);
            var name = (flags & 0x800) != 0 ? Encoding.UTF8.GetString(nameBytes) : Encoding.Latin1.GetString(nameBytes);

            // Zip64 extended information: only the fields that overflowed are present, in this order.
            var extra = data.Slice(position + 46 + nameLength, extraLength);
            while (extra.Length >= 4)
            {
                var id = ReadUInt16(extra, 0);
                var size = ReadUInt16(extra, 2);
                if (4 + size > extra.Length)
                    break;

                if (id == 0x0001)
                {
                    var field = extra.Slice(4, size);
                    var offset = 0;
                    if (uncompressedSize == uint.MaxValue && offset + 8 <= field.Length) { uncompressedSize = checked((long)ReadUInt64(field, offset)); offset += 8; }
                    if (compressedSize == uint.MaxValue && offset + 8 <= field.Length) { compressedSize = checked((long)ReadUInt64(field, offset)); offset += 8; }
                    if (localHeaderOffset == uint.MaxValue && offset + 8 <= field.Length) { localHeaderOffset = checked((long)ReadUInt64(field, offset)); }
                }

                extra = extra[(4 + size)..];
            }

            entries.Add(new RemoteZipEntry(name, flags, method, compressedSize, uncompressedSize, localHeaderOffset));
            position = end;
        }

        return entries;
    }

    private async Task<(byte[] Data, long TotalLength)> ReadTailAsync(Uri uri, int maxLength, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Range = new RangeHeaderValue(null, maxLength);

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        EnsureSuccess(response, uri);

        var data = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.OK)
            return (data, data.Length); // the whole archive is smaller than the requested suffix

        var total = response.Content.Headers.ContentRange?.Length
                    ?? throw new InvalidDataException($"{uri}: server did not report the archive size.");
        return (data, total);
    }

    private async Task<byte[]> ReadRangeAsync(
        Uri uri, long offset, int length, IProgress<DownloadProgress>? progress, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Range = new RangeHeaderValue(offset, offset + length - 1);

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        EnsureSuccess(response, uri);
        if (response.StatusCode != HttpStatusCode.PartialContent)
            throw new NotSupportedException($"{uri.Host} does not support HTTP range requests.");

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var buffer = new byte[length];
        var received = 0;
        progress?.Report(new DownloadProgress(0, length));
        while (received < length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(received, Math.Min(81920, length - received)), cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
                throw new IOException($"{uri}: connection closed after {received} of {length} bytes.");

            received += read;
            progress?.Report(new DownloadProgress(received, length));
        }

        return buffer;
    }

    private static void EnsureSuccess(HttpResponseMessage response, Uri uri)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"{uri} returned {(int)response.StatusCode} {response.ReasonPhrase}.", null, response.StatusCode);
        }
    }

    private static int? FindSignatureBackwards(ReadOnlySpan<byte> data, uint signature, int minRecordSize)
    {
        for (var i = data.Length - minRecordSize; i >= 0; i--)
        {
            if (ReadUInt32(data, i) == signature)
                return i;
        }

        return null;
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]);

    private static uint ReadUInt32(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);

    private static ulong ReadUInt64(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadUInt64LittleEndian(data[offset..]);
}
