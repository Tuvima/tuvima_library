using MediaEngine.Api.Services.View;
using MediaEngine.Domain.Services;
using MediaEngine.Storage.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;

namespace MediaEngine.Api.Tests;

public sealed class ViewThumbnailServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"tuvima-view-thumbnail-{Guid.NewGuid():N}");

    [Fact]
    public async Task GetOrCreateAsync_WritesManagedDerivative_WithoutChangingOriginal()
    {
        Directory.CreateDirectory(_root);
        var sourcePath = Path.Combine(_root, "source.jpg");
        WriteJpeg(sourcePath, 1200, 800);
        var originalBytes = await File.ReadAllBytesAsync(sourcePath);
        var originalWriteTime = File.GetLastWriteTimeUtc(sourcePath);
        var itemId = Guid.NewGuid();
        var paths = new AssetPathService(_root);
        var service = new ViewThumbnailService(paths, NullLogger<ViewThumbnailService>.Instance);

        var first = await service.GetOrCreateAsync(itemId, Location(itemId, sourcePath));

        Assert.NotNull(first);
        Assert.True(File.Exists(first));
        Assert.StartsWith(paths.DerivedRoot, first, StringComparison.OrdinalIgnoreCase);
        using (var thumbnail = SKBitmap.Decode(first))
        {
            Assert.NotNull(thumbnail);
            Assert.Equal(640, Math.Max(thumbnail.Width, thumbnail.Height));
        }
        Assert.Equal(originalBytes, await File.ReadAllBytesAsync(sourcePath));
        Assert.Equal(originalWriteTime, File.GetLastWriteTimeUtc(sourcePath));

        var derivativeWriteTime = File.GetLastWriteTimeUtc(first);
        var second = await service.GetOrCreateAsync(itemId, Location(itemId, sourcePath));
        Assert.Equal(first, second);
        Assert.Equal(derivativeWriteTime, File.GetLastWriteTimeUtc(first));
    }

    [Fact]
    public async Task GetOrCreateAsync_ReturnsNull_ForNonImageContent()
    {
        Directory.CreateDirectory(_root);
        var sourcePath = Path.Combine(_root, "notes.txt");
        await File.WriteAllTextAsync(sourcePath, "personal media notes");
        var itemId = Guid.NewGuid();
        var service = new ViewThumbnailService(
            new AssetPathService(_root), NullLogger<ViewThumbnailService>.Instance);

        var result = await service.GetOrCreateAsync(
            itemId, Location(itemId, sourcePath) with { MimeType = "text/plain" });

        Assert.Null(result);
    }

    private static LocalAssetContentLocation Location(Guid itemId, string filePath) => new(
        itemId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, null,
        filePath, "image/jpeg", new FileInfo(filePath).Length, "hash", "primary", null);

    private static void WriteJpeg(string path, int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.DarkSlateBlue);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 90);
        using var output = File.Create(path);
        data.SaveTo(output);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
