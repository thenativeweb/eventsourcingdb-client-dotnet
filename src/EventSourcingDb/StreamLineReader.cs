using System;
using System.Buffers;
using System.IO;
using System.IO.Pipelines;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EventSourcingDb;

internal sealed class StreamLineReader : IAsyncDisposable
{
    private readonly PipeReader _pipeReader;
    private bool _isCompleted;

    public StreamLineReader(Stream stream)
    {
        _pipeReader = PipeReader.Create(stream, new StreamPipeReaderOptions(
            bufferSize: 4096,
            minimumReadSize: 1024,
            leaveOpen: true
        ));
    }

    public async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken = default)
    {
        if (_isCompleted)
        {
            return null;
        }

        while (true)
        {
            var result = await _pipeReader.ReadAsync(cancellationToken).ConfigureAwait(false);
            var buffer = result.Buffer;

            var newlinePosition = buffer.PositionOf((byte)'\n');

            if (newlinePosition != null)
            {
                var lineSequence = buffer.Slice(0, newlinePosition.Value);
                var line = GetString(lineSequence);

                var nextPosition = buffer.GetPosition(1, newlinePosition.Value);
                _pipeReader.AdvanceTo(nextPosition);

                return line.TrimEnd('\r');
            }

            if (result.IsCompleted)
            {
                _isCompleted = true;

                if (buffer.Length > 0)
                {
                    var lastLine = GetString(buffer);
                    _pipeReader.AdvanceTo(buffer.End);
                    return lastLine.TrimEnd('\r');
                }

                _pipeReader.AdvanceTo(buffer.End);
                return null;
            }

            _pipeReader.AdvanceTo(buffer.Start, buffer.End);
        }
    }

    private static string GetString(ReadOnlySequence<byte> sequence)
    {
        if (sequence.IsSingleSegment)
        {
            return Encoding.UTF8.GetString(sequence.FirstSpan);
        }

        return Encoding.UTF8.GetString(sequence.ToArray());
    }

    public async ValueTask DisposeAsync()
    {
        await _pipeReader.CompleteAsync().ConfigureAwait(false);
    }
}
