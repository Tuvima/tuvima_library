using System.Net;
using MediaEngine.Domain.Services;

namespace MediaEngine.Domain.Tests;

public sealed class BoundedHttpContentTests
{
    [Fact]
    public async Task ReadImageAsync_ReturnsContentWithinLimit()
    {
        var expected = new byte[] { 1, 2, 3, 4 };
        using var content = new ByteArrayContent(expected);

        var actual = await BoundedHttpContent.ReadImageAsync(content);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task ReadImageAsync_RejectsDeclaredOversizeBeforeReading()
    {
        using var content = new ByteArrayContent([1]);
        content.Headers.ContentLength = BoundedHttpContent.MaximumImageBytes + 1L;

        await Assert.ThrowsAsync<RemoteContentTooLargeException>(
            () => BoundedHttpContent.ReadImageAsync(content));
    }

    [Fact]
    public async Task ReadImageAsync_RejectsStreamingOversizeWithoutContentLength()
    {
        using var content = new UnknownLengthContent(BoundedHttpContent.MaximumImageBytes + 1);

        await Assert.ThrowsAsync<RemoteContentTooLargeException>(
            () => BoundedHttpContent.ReadImageAsync(content));
    }

    [Fact]
    public async Task CopyImageToFileAtomicallyAsync_ReplacesCompleteFile()
    {
        var directory = Directory.CreateTempSubdirectory("bounded-image-");
        try
        {
            var path = Path.Combine(directory.FullName, "cover.jpg");
            await File.WriteAllBytesAsync(path, [9]);
            await using var input = new MemoryStream([1, 2, 3]);

            await BoundedHttpContent.CopyImageToFileAtomicallyAsync(input, path);

            Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(path));
            Assert.Empty(Directory.EnumerateFiles(directory.FullName, "*.tmp"));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task CopyImageToFileAtomicallyAsync_OversizePreservesPreviousFile()
    {
        var directory = Directory.CreateTempSubdirectory("bounded-image-");
        try
        {
            var path = Path.Combine(directory.FullName, "cover.jpg");
            await File.WriteAllBytesAsync(path, [9]);
            await using var input = new MemoryStream(new byte[BoundedHttpContent.MaximumImageBytes + 1]);

            await Assert.ThrowsAsync<RemoteContentTooLargeException>(
                () => BoundedHttpContent.CopyImageToFileAtomicallyAsync(input, path));

            Assert.Equal(new byte[] { 9 }, await File.ReadAllBytesAsync(path));
            Assert.Empty(Directory.EnumerateFiles(directory.FullName, "*.tmp"));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private sealed class UnknownLengthContent(int length) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            stream.WriteAsync(new byte[length]).AsTask();

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }

        protected override Task<Stream> CreateContentReadStreamAsync() =>
            Task.FromResult<Stream>(new MemoryStream(new byte[length], writable: false));
    }
}
