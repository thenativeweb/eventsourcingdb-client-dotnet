using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;

namespace EventSourcingDb;

// The body of a write, which tells whether it was handed on to the connection. The HttpClient
// hands the body on only once it has established a connection and is sending the request, and
// every way of reading the body hands it on, so as long as it was not, nothing of the request
// can have reached the server: the connection could not be established, or the request was
// cancelled or stalled before it was sent.
internal sealed class WriteContent : HttpContent
{
    private readonly byte[] _body;
    private int _isHandedOn;

    public WriteContent(byte[] body)
    {
        _body = body;
        Headers.ContentType = new MediaTypeHeaderValue(MediaTypeNames.Application.Json) { CharSet = "utf-8" };
    }

    public bool IsHandedOn => Volatile.Read(ref _isHandedOn) == 1;

    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
    {
        return SerializeToStreamAsync(stream, context, CancellationToken.None);
    }

    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
    {
        Volatile.Write(ref _isHandedOn, 1);
        await stream.WriteAsync(_body, cancellationToken).ConfigureAwait(false);
    }

    protected override void SerializeToStream(Stream stream, TransportContext? context, CancellationToken cancellationToken)
    {
        Volatile.Write(ref _isHandedOn, 1);
        stream.Write(_body);
    }

    protected override bool TryComputeLength(out long length)
    {
        length = _body.Length;
        return true;
    }
}
