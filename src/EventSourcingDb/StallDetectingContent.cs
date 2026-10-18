using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace EventSourcingDb;

// Wraps the content of a response, so that every read from it fails with the exception that
// createStallException creates once nothing arrives from the server for the stall timeout. Each
// read starts the time anew, so the time that a caller spends between two reads does not count.
internal sealed class StallDetectingContent : HttpContent
{
    private readonly HttpContent _content;
    private readonly TimeSpan _stallTimeout;
    private readonly Func<Exception> _createStallException;

    public StallDetectingContent(HttpContent content, TimeSpan stallTimeout, Func<Exception> createStallException)
    {
        _content = content;
        _stallTimeout = stallTimeout;
        _createStallException = createStallException;

        foreach (var header in content.Headers)
        {
            Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
    }

    protected override async Task<Stream> CreateContentReadStreamAsync(CancellationToken cancellationToken)
    {
        var stream = await _content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

        return new StallDetectingStream(stream, _stallTimeout, _createStallException);
    }

    protected override Task<Stream> CreateContentReadStreamAsync()
    {
        return CreateContentReadStreamAsync(CancellationToken.None);
    }

    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
    {
        await using var source = await CreateContentReadStreamAsync(cancellationToken).ConfigureAwait(false);
        await source.CopyToAsync(stream, cancellationToken).ConfigureAwait(false);
    }

    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
    {
        return SerializeToStreamAsync(stream, context, CancellationToken.None);
    }

    protected override bool TryComputeLength(out long length)
    {
        length = 0;
        return false;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _content.Dispose();
        }

        base.Dispose(disposing);
    }

    private sealed class StallDetectingStream(Stream stream, TimeSpan stallTimeout, Func<Exception> createStallException) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            using var stallSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            stallSource.CancelAfter(stallTimeout);

            try
            {
                return await stream.ReadAsync(buffer, stallSource.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stallSource.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                throw createStallException();
            }
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                stream.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
