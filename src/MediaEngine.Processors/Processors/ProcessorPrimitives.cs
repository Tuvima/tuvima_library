using MediaEngine.Domain.Enums;
using MediaEngine.Processors.Models;

namespace MediaEngine.Processors.Processors;

internal static class ProcessorHeaderReader
{
    public static bool TryRead(
        string filePath,
        Span<byte> destination,
        out int bytesRead,
        FileShare fileShare = FileShare.Read)
    {
        try
        {
            using var stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                fileShare,
                bufferSize: destination.Length,
                FileOptions.None);

            bytesRead = stream.Read(destination);
            return true;
        }
        catch (IOException)
        {
            bytesRead = 0;
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            bytesRead = 0;
            return false;
        }
    }
}

internal static class ProcessorClaimFactory
{
    public static ExtractedClaim Create(
        string key,
        string value,
        double confidence,
        bool trimValue = false) => new()
        {
            Key = key,
            Value = trimValue ? value.Trim() : value,
            Confidence = confidence,
        };
}

internal static class ProcessorResultFactory
{
    public static ProcessorResult Corrupt(
        string filePath,
        MediaType detectedType,
        string reason) => new()
        {
            FilePath = filePath,
            DetectedType = detectedType,
            IsCorrupt = true,
            CorruptReason = reason,
        };
}
