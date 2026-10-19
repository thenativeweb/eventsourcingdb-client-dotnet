using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;

namespace EventSourcingDb;

// The body of a write, which tells whether it was handed on, towards a connection or anywhere
// else. The HttpClient hands the body on only once it has established a connection and is
// sending the request, and every way of reading the body hands it on, so as long as it was not,
// nothing of the request can have reached the server: the connection could not be established,
// or the request was cancelled or stalled before it was sent.
//
// Something in the HTTP pipeline that sends the body again, such as a handler that retries
// requests after the answer of the first attempt was lost, could have the write stored twice. So
// the body serializes itself only once, and refuses every further attempt before anything of it
// reaches the server. The refusal is a NotSupportedException, which the HttpClient passes on as
// it is, unlike an InvalidOperationException, which it wraps in an HttpRequestException, so that
// a handler that retries requests after an HttpRequestException does not retry the refusal.
internal sealed class WriteContent : HttpContent
{
    private const string SentAgainMessage = "The HTTP pipeline tried to send the body of the write a second time, e.g. a handler that retries requests, follows a redirect, or reads the body, which the client SDK refused, so that the write cannot be stored twice.";

    private readonly byte[] _body;
    private int _handOnCount;
    private int _serializationCount;
    private int _streamCount;
    private Exception? _serializationFailure;
    private Exception? _sentAgainRefusal;
    private bool _isProbingForBuffer;
    private bool _wasSerializedWhileProbingForBuffer;

    public WriteContent(byte[] body)
    {
        _body = body;
        Headers.ContentType = new MediaTypeHeaderValue(MediaTypeNames.Application.Json) { CharSet = "utf-8" };
    }

    public bool IsHandedOn => Volatile.Read(ref _handOnCount) > 0;

    // If something tried to send the body a second time, which it refused, what failed the first
    // attempt, as far as the body can tell, and null otherwise. The first attempt may have been
    // stored then, whatever the write failed with afterwards. The body sees how the first attempt
    // failed only if handing itself on failed. If the attempt failed afterwards, for example
    // because its answer was lost, or because a handler discarded an answer such as a status of
    // 503, all that the body knows is that it refused to be sent again.
    public Exception? SentAgainCause
    {
        get
        {
            var refusal = Volatile.Read(ref _sentAgainRefusal);

            return refusal is null ? null : Volatile.Read(ref _serializationFailure) ?? refusal;
        }
    }

    // Whether the write may have reached the server more than once, as when a handler sent it
    // again after an attempt whose answer was lost. The body serializes itself only once, but if
    // it was read as a stream, or read into a buffer, every later attempt reads from that stream
    // or copies from that buffer, which the body can neither count nor refuse, so it has to
    // assume that there were several.
    public async Task<bool> MayHaveBeenSentMoreThanOnceAsync()
    {
        if (Volatile.Read(ref _streamCount) > 0)
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
        var isAttempt = HandOnBySerializing();

        try
        {
            await stream.WriteAsync(_body, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (isAttempt)
        {
            Volatile.Write(ref _serializationFailure, exception);
            throw;
        }
    }

    protected override void SerializeToStream(Stream stream, TransportContext? context, CancellationToken cancellationToken)
    {
        var isAttempt = HandOnBySerializing();

        try
        {
            stream.Write(_body);
        }
        catch (Exception exception) when (isAttempt)
        {
            Volatile.Write(ref _serializationFailure, exception);
            throw;
        }
    }

    protected override Task<Stream> CreateContentReadStreamAsync()
    {
        return Task.FromResult(CreateContentReadStream(CancellationToken.None));
    }

    protected override Task<Stream> CreateContentReadStreamAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(CreateContentReadStream(cancellationToken));
    }

    // A stream goes to whoever reads it rather than to a connection, so reading the body as a
    // stream is never refused, not even after the body was sent.
    protected override Stream CreateContentReadStream(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _handOnCount);
        Interlocked.Increment(ref _streamCount);

        return new MemoryStream(_body, writable: false);
    }

    protected override bool TryComputeLength(out long length)
    {
        length = _body.Length;
        return true;
    }

    // Counts that the body serializes itself for an attempt of the write, and refuses to do so a
    // second time. Returns false if the body only probes whether it is buffered, which is no
    // attempt of the write.
    private bool HandOnBySerializing()
    {
        if (_isProbingForBuffer)
        {
            _wasSerializedWhileProbingForBuffer = true;
            return false;
        }

        if (Interlocked.Increment(ref _serializationCount) > 1)
        {
            var refusal = new NotSupportedException(SentAgainMessage);
            Interlocked.CompareExchange(ref _sentAgainRefusal, refusal, null);

            throw refusal;
        }

        Interlocked.Increment(ref _handOnCount);
        return true;
    }
}
