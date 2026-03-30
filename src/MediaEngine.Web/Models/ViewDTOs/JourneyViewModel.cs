using MediaEngine.Domain.Services;
using MT = MediaEngine.Domain.Enums.MediaType;

namespace MediaEngine.Web.Models.ViewDTOs;

/// <summary>
/// A media item the user has been reading/watching/listening to,
/// with progress data for the "Continue your Journey" hero.
/// </summary>
public sealed class JourneyItemViewModel
{
    public Guid    AssetId         { get; init; }
    public Guid    WorkId          { get; init; }
    public Guid?   HubId           { get; init; }
    public string  Title           { get; init; } = string.Empty;
    public string? Author          { get; init; }
    public string? CoverUrl        { get; init; }
    public string? HeroUrl         { get; init; }
    public string? Narrator        { get; init; }
    public string? Series          { get; init; }
    public string? SeriesPosition  { get; init; }
    public string? Description     { get; init; }
    public string  MediaType       { get; init; } = string.Empty;
    public double  ProgressPct     { get; init; }
    public DateTimeOffset LastAccessed { get; init; }
    public string? HubDisplayName  { get; init; }
    public Dictionary<string, string> ExtendedProperties { get; init; } = [];

    // ── Display helpers ─────────────────────────────────────────────────

    public string ProgressDisplay => ProgressPct switch
    {
        >= 99.5 => "Complete",
        > 0     => $"{Math.Max(1, ProgressPct):F0}%",
        _       => "Not started",
    };

    public string ActionVerb => MediaTypeClassifier.Classify(MediaType) switch
    {
        MT.Books or MT.Comics   => "Continue Reading",
        MT.Audiobooks           => "Continue Listening",
        MT.Movies or MT.TV      => "Continue Watching",
        MT.Music or MT.Podcasts => "Continue Listening",
        _                       => "Continue",
    };

    /// <summary>Button label including progress percentage, matching HubDetail's PrimaryActionLabel style.</summary>
    public string ActionLabel => ProgressPct is > 0 and < 99.5
        ? $"{ActionVerb} · {Math.Max(1, ProgressPct):F0}%"
        : ActionVerb;

    public string FormatMediaType => MediaTypeClassifier.GetDisplayLabel(MediaType);
}
