using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace AstroNucleaus.Infrastructure.Tests.Endf;

/// <summary>
/// In-memory HTTP server for one file that honours Range headers like a real web server.
/// </summary>
internal sealed class RangeServer : HttpMessageHandler
{
    private readonly byte[] _content;

    public RangeServer(byte[] content)
    {
        _content = content;
    }

    public List<RangeHeaderValue?> Requests { get; } = [];

    public long BytesServed { get; private set; }

    public static byte[] CreateZip(params (string Name, string Content)[] entries)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                using var writer = new StreamWriter(archive.CreateEntry(name, CompressionLevel.Optimal).Open(), Encoding.ASCII);
                writer.Write(content);
            }
        }

        return buffer.ToArray();
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request.Headers.Range);

        var range = request.Headers.Range?.Ranges.SingleOrDefault();
        if (range is null)
            return Task.FromResult(Respond(HttpStatusCode.OK, 0, _content.Length));

        long from, to;
        if (range.From is null)
        {
            from = Math.Max(0, _content.Length - range.To!.Value);
            to = _content.Length - 1;
        }
        else
        {
            from = range.From.Value;
            to = Math.Min(range.To ?? _content.Length - 1, _content.Length - 1);
        }

        var response = Respond(HttpStatusCode.PartialContent, from, (int)(to - from + 1));
        response.Content.Headers.ContentRange = new ContentRangeHeaderValue(from, to, _content.Length);
        return Task.FromResult(response);
    }

    private HttpResponseMessage Respond(HttpStatusCode status, long offset, int length)
    {
        BytesServed += length;
        return new HttpResponseMessage(status) { Content = new ByteArrayContent(_content, (int)offset, length) };
    }
}
