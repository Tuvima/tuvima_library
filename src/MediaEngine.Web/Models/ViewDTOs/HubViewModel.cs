using MediaEngine.Web.Services.Integration;

namespace MediaEngine.Web.Models.ViewDTOs;

/// <summary>
/// UI representation of a Hub (media collection).
/// Maps from the API's HubDto; adds display-friendly helper properties.
/// </summary>
public sealed class HubViewModel
{
    public Guid                Id               { get; init; }
    public Guid?               UniverseId       { get; init; }
    public DateTimeOffset      CreatedAt        { get; init; }
    public List<WorkViewModel> Works            { get; init; } = [];

    // ── Parent Hub / franchise hierarchy ────────────────────────────────────

    /// <summary>ID of the franchise-level Parent Hub, if any.</summary>
    public Guid? ParentHubId { get; init; }

    /// <summary>Display name of the Parent Hub (for breadcrumb rendering).</summary>
    public string? ParentHubName { get; init; }

    /// <summary>Number of child Hubs under this Hub (when acting as a Parent Hub).</summary>
    public int ChildHubCount { get; init; }

    /// <summary>True when this Hub is a Parent Hub (has children).</summary>
    public bool IsParentHub => ChildHubCount > 0;

    // ── Display helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Hub-level display name set by the Engine at organise time (e.g. the
    /// work title or series name stored in <c>hubs.display_name</c>).
    /// Falls back to the first Work's title when the Engine hasn't set one yet.
    /// </summary>
    public string? HubDisplayName { get; init; }

    /// <summary>Best title: Hub's own name first, then first Work's title, then short ID.</summary>
    public string DisplayName =>
        (!string.IsNullOrWhiteSpace(HubDisplayName) ? HubDisplayName : null)
        ?? Works.Select(GetTitle).FirstOrDefault(t => !string.IsNullOrEmpty(t))
        ?? $"Hub {Id:N}"[..12];

    public int    WorkCount  => Works.Count;
    public string MediaTypes => string.Join(", ", Works.Select(w => w.MediaType).Distinct());
    public bool   HasWorks   => Works.Count > 0;

    /// <summary>Cover art URL from the first Work's canonical "cover" value (external provider URL).</summary>
    public string? CoverUrl => Works.Select(w => w.CoverUrl).FirstOrDefault(u => !string.IsNullOrEmpty(u));

    /// <summary>Pre-rendered hero banner URL from the first Work's canonical "hero" value.</summary>
    public string? HeroUrl => Works.Select(w => w.HeroUrl).FirstOrDefault(u => !string.IsNullOrEmpty(u));

    /// <summary>Release year from the first Work with a year value.</summary>
    public string? Year => Works.Select(w => w.Year).FirstOrDefault(y => !string.IsNullOrEmpty(y));

    /// <summary>
    /// All distinct media types across the Hub's Works (e.g. "Epub, Audiobooks").
    /// Use this for display badges. For single-work Hubs this will be one value.
    /// </summary>
    public string? PrimaryMediaType =>
        Works.Select(w => w.MediaType)
             .Where(t => !string.IsNullOrWhiteSpace(t))
             .Distinct()
             .FirstOrDefault();

    /// <summary>Best description across all works.</summary>
    public string? Description => Works.Select(w => w.Description).FirstOrDefault(d => !string.IsNullOrEmpty(d));

    /// <summary>Attribution string for the description (e.g. "Wikipedia (CC BY-SA 4.0)").</summary>
    public string? DescriptionSource => Works.Select(w => w.DescriptionSource).FirstOrDefault(s => !string.IsNullOrEmpty(s));

    /// <summary>Best author/creator across all works.</summary>
    public string? Author => Works.Select(w => w.Author).FirstOrDefault(a => !string.IsNullOrEmpty(a));

    /// <summary>Author QID from the first Work with an author QID.</summary>
    public string? AuthorQid => Works.Select(w => w.AuthorQid).FirstOrDefault(q => !string.IsNullOrEmpty(q));

    /// <summary>Genre tags from the first Work with genre data.</summary>
    public string? Genre => Works.Select(w => w.Genre).FirstOrDefault(g => !string.IsNullOrEmpty(g));

    /// <summary>Genre as an array of individual values from the first Work with genres.</summary>
    public IReadOnlyList<string> Genres =>
        Works.Select(w => w.Genres).FirstOrDefault(g => g.Count > 0) ?? [];

    /// <summary>Genre QIDs matching <see cref="Genres"/> by position.</summary>
    public IReadOnlyList<string> GenreQids =>
        Works.Select(w => w.GenreQids).FirstOrDefault(g => g.Count > 0) ?? [];

    /// <summary>Series name from the first Work with series data.</summary>
    public string? Series => Works.Select(w => w.Series).FirstOrDefault(s => !string.IsNullOrEmpty(s));

    /// <summary>Rating from the first Work with a rating.</summary>
    public string? Rating => Works.Select(w => w.Rating).FirstOrDefault(r => !string.IsNullOrEmpty(r));

    /// <summary>Fictional universe QID from the first Work with a universe link (for Chronicle Explorer navigation).</summary>
    public string? FictionalUniverseQid => Works.Select(w => w.FictionalUniverseQid).FirstOrDefault(q => !string.IsNullOrEmpty(q));

    /// <summary>Best human-facing identifier from the first Work with one.</summary>
    public string? DisplayIdentifier =>
        Works.Select(w => w.DisplayIdentifier).FirstOrDefault(id => !string.IsNullOrEmpty(id));

    /// <summary>Label for <see cref="DisplayIdentifier"/>.</summary>
    public string DisplayIdentifierLabel =>
        Works.Select(w => w.DisplayIdentifierLabel).FirstOrDefault(l => l != "QID") ?? "QID";

    /// <summary>
    /// Brand hex colour derived from the dominant media type across this Hub's Works.
    /// Driven by <see cref="UniverseMapper.ColourForHub"/> so all colour logic
    /// stays in one place (the mapper) and Hubs are colour-blind by design.
    /// </summary>
    public string DominantHexColor { get; init; } = "#9E9E9E";

    // ── Factory ───────────────────────────────────────────────────────────────

    public static HubViewModel FromApiDto(
        Guid id, Guid? universeId, DateTimeOffset createdAt, IEnumerable<WorkViewModel> works,
        string? displayName = null, Guid? parentHubId = null, string? parentHubName = null, int childHubCount = 0)
    {
        var workList = works.ToList();
        return new()
        {
            Id               = id,
            UniverseId       = universeId,
            CreatedAt        = createdAt,
            Works            = workList,
            HubDisplayName   = displayName,
            DominantHexColor = UniverseMapper.ColourForHub(workList),
            ParentHubId      = parentHubId,
            ParentHubName    = parentHubName,
            ChildHubCount    = childHubCount,
        };
    }

    private static string? GetTitle(WorkViewModel w)
    {
        var raw = w.CanonicalValues.FirstOrDefault(cv => cv.Key == "title")?.Value;
        if (raw is not null && raw.Contains("|||", StringComparison.Ordinal))
            return raw.Split("|||", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        return raw;
    }
}
