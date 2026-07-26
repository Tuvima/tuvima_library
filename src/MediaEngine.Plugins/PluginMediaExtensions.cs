namespace MediaEngine.Plugins;

/// <summary>
/// Shared file-extension sets for plugin media detection. Consolidates the byte-identical
/// private <c>VideoExtensions</c> HashSets previously duplicated in
/// <c>CommercialSkipSegmentDetector</c> (MediaEngine.Plugin.CommercialSkip) and
/// <c>FfmpegSegmentDetector</c> (MediaEngine.Plugin.MediaSegments).
/// </summary>
public static class PluginMediaExtensions
{
    /// <summary>
    /// Video file extensions (including the leading dot) recognized by playback-segment
    /// detector plugins.
    /// </summary>
    public static readonly IReadOnlySet<string> Video = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".m4v", ".mkv", ".webm", ".avi", ".ts", ".mpeg", ".mpg",
    };
}
