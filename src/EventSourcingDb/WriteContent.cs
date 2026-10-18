using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;

namespace EventSourcingDb;

// The body of a write, which tells how often it was handed on, towards a connection or anywhere
// else. The HttpClient hands the body on only once it has established a connection and is
// sending the request, and every way of reading the body hands it on, so as long as it was not,
// nothing of the request can have reached the server: the connection could not be established,
// or the request was cancelled or stalled before it was sent.
internal sealed class WriteContent : HttpContent
{
    private readonly byte[] _body;
    private int _handOnCount;
    private int _streamCount;
    private bool _isProbingForBuffer;
    private bool _wasSerializedWhileProbingForBuffer;

    public WriteContent(byte[] body)
    {
        _body = body;
        Headers.ContentType = new MediaTypeHeaderValue(MediaTypeNames.Application.Json) { CharSet = "utf-8" };
    }

    public bool IsHandedOn => Volatile.Read(ref _handOnCount) > 0;

    // Whether the write may have reached the server more than once, as when a handler sent it
    // again after an attempt whose answer was lost. That is so if the body was handed on more
    // than once. It is also so if it was read as a stream, or read into a buffer, since every
    // later attempt then reads from that stream or copies from that buffer, which the body cannot
    // count, so it has to assume that there were several.
    public async Task<bool> MayHaveBeenSentMoreThanOnceAsync()
    {
        if (Volatile.Read(ref _handOnCount) > 1 || Volatile.Read(ref _streamCount) > 0)
        {
            return true;
        }

        // Content that is not buffered yet serializes itself to be loaded into a buffer, while
        // buffered content does not. The request has ended, so buffering it now harms nothing.
        _isProbingForBuffer = true;
        try
        {
            await LoadIntoBufferAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            return true;
        }
        finally
        {
            _isProbingForBuffer = false;
        }

        return !_wasSerializedWhileProbingForBuffer;
    }

    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
    {
        return SerializeToStreamAsync(stream, context, CancellationToken.None);
    }

    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
    {
        CountHandOn();
        await stream.WriteAsync(_body, cancellationToken).ConfigureAwait(false);
    }

    protected override void SerializeToStream(Stream stream, TransportContext? context, CancellationToken cancellationToken)
    {
        CountHandOn();
        stream.Write(_body);
    }

    protected override Task<Stream> CreateContentReadStreamAsync()
    {
        return Task.FromResult(CreateContentReadStream(CancellationToken.None));
    }

    protected override Task<Stream> CreateContentReadStreamAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(CreateContentReadStream(cancellationToken));
    }

    protected override Stream CreateContentReadStream(CancellationToken cancellationToken)
    {
        CountHandOn();
        Interlocked.Increment(ref _streamCount);

        return new MemoryStream(_body, writable: false);
    }

    protected override bool TryComputeLength(out long length)
    {
        length = _body.Length;
        return true;
    }

    private void CountHandOn()
    {
        if (_isProbingForBuffer)
        {
            _wasSerializedWhileProbingForBuffer = true;
            return;
        }

        Interlocked.Increment(ref _handOnCount);
    }
}
