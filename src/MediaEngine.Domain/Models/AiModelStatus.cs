namespace MediaEngine.Domain.Models;

/// <summary>
/// Status of a single AI model, identified by its role.
/// </summary>
public sealed class AiModelStatus
{
    /// <summary>The functional role this model serves.</summary>
    public required Enums.AiModelRole Role { get; init; }

    /// <summary>The model type (Text or Audio).</summary>
    public required Enums.AiModelType ModelType { get; init; }

    /// <summary>Current lifecycle state.</summary>
    public required Enums.AiModelState State { get; init; }

    /// <summary>Model filename on disk (e.g. "llama-3.2-3b-instruct.Q4_K_M.gguf").</summary>
    public required string ModelFile { get; init; }

    /// <summary>Model file size in megabytes (from config, not measured).</summary>
    public int SizeMB { get; init; }

    /// <summary>Download progress percentage (0-100). Only meaningful when State is Downloading.</summary>
    public int DownloadProgressPercent { get; init; }

    /// <summary>Bytes downloaded so far. Only meaningful when State is Downloading.</summary>
    public long BytesDownloaded { get; init; }

    /// <summary>Total bytes to download. Only meaningful when State is Downloading.</summary>
    public long TotalBytes { get; init; }

    /// <summary>Error message if State is Error, otherwise null.</summary>
    public string? ErrorMessage { get; init; }
}
