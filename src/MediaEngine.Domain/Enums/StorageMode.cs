namespace MediaEngine.Domain.Enums;

/// <summary>
/// Controls where Tuvima-managed assets are stored.
/// </summary>
public enum StorageMode
{
    /// <summary>
    /// Manager-owned assets live centrally while playback-facing sidecars stay local.
    /// </summary>
    Hybrid,

    /// <summary>
    /// All managed assets live under the central app data tree.
    /// </summary>
    Centralized,

    /// <summary>
    /// Managed assets are written beside media files or media folders.
    /// </summary>
    CoLocated,
}
