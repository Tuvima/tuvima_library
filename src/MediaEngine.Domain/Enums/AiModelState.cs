namespace MediaEngine.Domain.Enums;

/// <summary>
/// Lifecycle state of an AI model.
/// </summary>
public enum AiModelState
{
    /// <summary>Model file has not been downloaded to the models directory.</summary>
    NotDownloaded = 0,

    /// <summary>Model file is currently being downloaded.</summary>
    Downloading = 1,

    /// <summary>Model file exists on disk and is ready to load.</summary>
    Ready = 2,

    /// <summary>Model is loaded into memory and available for inference.</summary>
    Loaded = 3,

    /// <summary>Model is being unloaded from memory.</summary>
    Unloading = 4,

    /// <summary>Model encountered an error (download failed, load failed, corrupt file).</summary>
    Error = 5,
}
