using System.Collections.Concurrent;
using MediaEngine.Domain.Services;
using MediaEngine.Storage.Contracts;
using SkiaSharp;

namespace MediaEngine.Api.Services.View;

/// <summary>
/// Produces managed timeline derivatives without modifying personal originals.
/// A derivative is refreshed only when its source file is newer.
/// </summary>
public sealed class ViewThumbnailService(
    AssetPathService assetPaths,
    ILogger<ViewThumbnailService> logger)
{
    private const int MaximumEdge = 640;
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _itemLocks = new();

    public async Task<string?> GetOrCreateAsync(
        Guid itemId,
        LocalAssetContentLocation source,
        CancellationToken ct = default)
    {
        if (itemId == Guid.Empty
            || !source.MimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
            || !File.Exists(source.FilePath))
        {
            return null;
        }

        var target = assetPaths.GetCentralDerivedPath(
            "local-item",
            itemId,
            "thumbnail",
            $"timeline-{MaximumEdge}.jpg");
        if (IsCurrent(target, source.FilePath)) return target;

        var gate = _itemLocks.GetOrAdd(itemId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (IsCurrent(target, source.FilePath)) return target;
            return await Task.Run(() => Generate(source.FilePath, target), ct).ConfigureAwait(false)
                ? target
                : null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Could not create the managed View thumbnail for item {ItemId}.", itemId);
            return null;
        }
        finally
        {
            gate.Release();
        }
    }

    private static bool IsCurrent(string target, string source) =>
        File.Exists(target)
        && File.GetLastWriteTimeUtc(target) >= File.GetLastWriteTimeUtc(source);

    private static bool Generate(string source, string target)
    {
        using var input = File.Open(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var bitmap = SKBitmap.Decode(input);
        if (bitmap is null || bitmap.Width <= 0 || bitmap.Height <= 0) return false;

        var scale = Math.Min(1d, MaximumEdge / (double)Math.Max(bitmap.Width, bitmap.Height));
        using var resized = bitmap.Resize(new SKImageInfo(
            Math.Max(1, (int)Math.Round(bitmap.Width * scale)),
            Math.Max(1, (int)Math.Round(bitmap.Height * scale))),
            new SKSamplingOptions(SKFilterMode.Linear));
        using var image = SKImage.FromBitmap(resized ?? bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 82);
        if (data is null) return false;

        var directory = Path.GetDirectoryName(target)!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".{Path.GetFileName(target)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var output = File.Open(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                data.SaveTo(output);
            }
            File.Move(temporary, target, overwrite: true);
            return true;
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
