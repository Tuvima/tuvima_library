using System.Buffers;

namespace MediaEngine.Domain.Services;

/// <summary>
/// Reads remote content into memory while enforcing a hard limit even when the
/// server omits or lies about Content-Length.
/// </summary>
public static class BoundedHttpContent
{
    public const int MaximumImageBytes = 20 * 1024 * 1024;

    public static async Task<byte[]> ReadImageAsync(
        HttpContent content,
        CancellationToken ct = default)
    {
        if (content.Headers.ContentLength is > MaximumImageBytes)
            throw new RemoteContentTooLargeException(MaximumImageBytes);

        await using var input = await content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var output = new MemoryStream(
            content.Headers.ContentLength is > 0
                ? (int)Math.Min(content.Headers.ContentLength.Value, MaximumImageBytes)
                : 0);
        var buffer = ArrayPool<byte>.Shared.Rent(81_920);
        try
        {
            while (true)
            {
                var read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false);
                if (read == 0)
                    break;
                if (output.Length + read > MaximumImageBytes)
                    throw new RemoteContentTooLargeException(MaximumImageBytes);
                await output.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            }

            return output.ToArray();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public static async Task WriteFileAtomicallyAsync(
        string destinationPath,
        ReadOnlyMemory<byte> content,
        CancellationToken ct = default)
    {
        var directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
        var temporaryPath = $"{destinationPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllBytesAsync(temporaryPath, content.ToArray(), ct).ConfigureAwait(false);
            File.Move(temporaryPath, destinationPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    public static async Task CopyImageToFileAtomicallyAsync(
        Stream input,
        string destinationPath,
        CancellationToken ct = default)
    {
        var directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
        var temporaryPath = $"{destinationPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var output = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81_920, useAsync: true))
            {
                var buffer = ArrayPool<byte>.Shared.Rent(81_920);
                try
                {
                    long total = 0;
                    while (true)
                    {
                        var read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false);
                        if (read == 0)
                            break;
                        total += read;
                        if (total > MaximumImageBytes)
                            throw new RemoteContentTooLargeException(MaximumImageBytes);
                        await output.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }
            }

            File.Move(temporaryPath, destinationPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }
}

public sealed class RemoteContentTooLargeException(int maximumBytes)
    : IOException($"Remote image exceeds the {maximumBytes / (1024 * 1024)} MB limit.");
