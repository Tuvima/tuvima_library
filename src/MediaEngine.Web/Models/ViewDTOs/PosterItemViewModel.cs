namespace MediaEngine.Web.Models.ViewDTOs;

/// <summary>
/// Adapter that unifies Hub and Work display for PosterCard/PosterSwimlane.
/// Home page uses FromHub(); lane pages use FromWork().
/// </summary>
public sealed record PosterItemViewModel
{
    public required Guid    Id            { get; init; }
    public required string  Title         { get; init; }
    public string?          Subtitle      { get; init; }
    public string?          CoverUrl      { get; init; }
    public string?          Year          { get; init; }
    public string?          FormatBadge   { get; init; }
    public bool             IsNew         { get; init; }
    public double?          Progress      { get; init; }
    public required string  NavigationUrl { get; init; }
    public string           DominantHexColor { get; init; } = "#1A2040";
    public PosterSourceType SourceType    { get; init; }

    public static PosterItemViewModel FromHub(HubViewModel hub) => new()
    {
        Id               = hub.Id,
        Title            = hub.DisplayName,
        Subtitle         = hub.Author ?? hub.Series,
        CoverUrl         = hub.CoverUrl,
        Year             = hub.Year,
        FormatBadge      = hub.PrimaryMediaType,
        IsNew            = DateTimeOffset.UtcNow - hub.CreatedAt < TimeSpan.FromDays(7),
        Progress         = null,
        NavigationUrl    = $"/hub/{hub.Id}",
        DominantHexColor = hub.DominantHexColor,
        SourceType       = PosterSourceType.Hub,
    };

    public static PosterItemViewModel FromWork(WorkViewModel work, string? fallbackCoverUrl = null, string? dominantHexColor = null) => new()
    {
        Id               = work.Id,
        Title            = work.Title,
        Subtitle         = work.Author,
        CoverUrl         = work.CoverUrl ?? fallbackCoverUrl,
        Year             = work.Year,
        FormatBadge      = work.MediaType,
        IsNew            = false,
        Progress         = null,
        NavigationUrl    = $"/book/{work.Id}",
        DominantHexColor = dominantHexColor ?? "#1A2040",
        SourceType       = PosterSourceType.Work,
    };

    public static PosterItemViewModel FromJourney(JourneyItemViewModel item) => new()
    {
        Id               = item.WorkId,
        Title            = item.Title,
        Subtitle         = item.Author,
        CoverUrl         = item.CoverUrl,
        Year             = null,
        FormatBadge      = item.MediaType,
        IsNew            = false,
        Progress         = item.ProgressPct > 0 ? item.ProgressPct : null,
        NavigationUrl    = $"/book/{item.WorkId}",
        DominantHexColor = "#1A2040",
        SourceType       = PosterSourceType.Work,
    };
}

public enum PosterSourceType { Hub, Work }
