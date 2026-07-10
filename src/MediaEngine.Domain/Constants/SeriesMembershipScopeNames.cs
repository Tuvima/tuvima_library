namespace MediaEngine.Domain.Constants;

/// <summary>
/// Structural scope of a manifest item relative to its immediate series.
/// </summary>
public static class SeriesMembershipScopeNames
{
    public const string MainSequence = "MainSequence";
    public const string Supplementary = "Supplementary";
    public const string CollectedContent = "CollectedContent";
    public const string BroaderContext = "BroaderContext";
    public const string Unpositioned = "Unpositioned";
}
