using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Xunit;

namespace EventSourcingDb.Tests;

public sealed class StreamLineReaderTests
{
    [Fact]
    public async Task ReadsSingleLine()
    {
        await using var stream = CreateStream("Hello, World!\n");
        await using var reader = new StreamLineReader(stream);

        var line = await reader.ReadLineAsync();

        Assert.Equal("Hello, World!", line);
    }

    [Fact]
    public async Task ReadsMultipleLines()
    {
        await using var stream = CreateStream("Line 1\nLine 2\nLine 3\n");
        await using var reader = new StreamLineReader(stream);

        var line1 = await reader.ReadLineAsync();
        var line2 = await reader.ReadLineAsync();
        var line3 = await reader.ReadLineAsync();
        var line4 = await reader.ReadLineAsync();

        Assert.Equal("Line 1", line1);
        Assert.Equal("Line 2", line2);
        Assert.Equal("Line 3", line3);
        Assert.Null(line4);
    }

    [Fact]
    public async Task ReadsLineWithCarriageReturnLineFeed()
    {
        await using var stream = CreateStream("Line with CRLF\r\n");
        await using var reader = new StreamLineReader(stream);

        var line = await reader.ReadLineAsync();

        Assert.Equal("Line with CRLF", line);
    }

    [Fact]
    public async Task ReadsLastLineWithoutNewline()
    {
        await using var stream = CreateStream("Last line without newline");
        await using var reader = new StreamLineReader(stream);

        var line = await reader.ReadLineAsync();
        var nullLine = await reader.ReadLineAsync();

        Assert.Equal("Last line without newline", line);
        Assert.Null(nullLine);
    }

    [Fact]
    public async Task ReturnsNullForEmptyStream()
    {
        await using var stream = CreateStream("");
        await using var reader = new StreamLineReader(stream);

        var line = await reader.ReadLineAsync();

        Assert.Null(line);
    }

    [Fact]
    public async Task ReadsEmptyLine()
    {
        await using var stream = CreateStream("\n");
        await using var reader = new StreamLineReader(stream);

        var line = await reader.ReadLineAsync();
        var nullLine = await reader.ReadLineAsync();

        Assert.Equal("", line);
        Assert.Null(nullLine);
    }

    [Fact]
    public async Task ReadsUtf8Characters()
    {
        await using var stream = CreateStream("Äöü ñ 中文 🎉\n");
        await using var reader = new StreamLineReader(stream);

        var line = await reader.ReadLineAsync();

        Assert.Equal("Äöü ñ 中文 🎉", line);
    }

    [Fact]
    public async Task ReadsJsonLine()
    {
        const string json = "{\"type\":\"event\",\"payload\":{\"id\":123}}\n";
        await using var stream = CreateStream(json);
        await using var reader = new StreamLineReader(stream);

        var line = await reader.ReadLineAsync();

        Assert.Equal("{\"type\":\"event\",\"payload\":{\"id\":123}}", line);
    }

    [Fact]
    public async Task ThrowsOnCancellation()
    {
        await using var stream = CreateStream("Line\n");
        await using var reader = new StreamLineReader(stream);

        var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await reader.ReadLineAsync(cts.Token);
        });
    }

    [Fact]
    public async Task ReturnsNullAfterStreamEnds()
    {
        await using var stream = CreateStream("Only line\n");
        await using var reader = new StreamLineReader(stream);

        var line1 = await reader.ReadLineAsync();
        var line2 = await reader.ReadLineAsync();
        var line3 = await reader.ReadLineAsync();

        Assert.Equal("Only line", line1);
        Assert.Null(line2);
        Assert.Null(line3);
    }

    [Fact]
    public async Task ReadsLongLine()
    {
        var longContent = new string('x', 10000);
        await using var stream = CreateStream(longContent + "\n");
        await using var reader = new StreamLineReader(stream);

        var line = await reader.ReadLineAsync();

        Assert.Equal(longContent, line);
    }

    [Fact]
    public async Task ReadsMixedLineEndings()
    {
        await using var stream = CreateStream("Line1\nLine2\r\nLine3\n");
        await using var reader = new StreamLineReader(stream);

        var line1 = await reader.ReadLineAsync();
        var line2 = await reader.ReadLineAsync();
        var line3 = await reader.ReadLineAsync();

        Assert.Equal("Line1", line1);
        Assert.Equal("Line2", line2);
        Assert.Equal("Line3", line3);
    }

    private static MemoryStream CreateStream(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        return new MemoryStream(bytes);
    }
}
