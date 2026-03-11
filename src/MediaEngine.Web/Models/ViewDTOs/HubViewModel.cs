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

    // ── Display helpers ───────────────────────────────────────────────────────

    /// <summary>Best title across all works, or a short ID fallback.</summary>
    public string DisplayName =>
        Works.Select(GetTitle).FirstOrDefault(t => !string.IsNullOrEmpty(t))
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

    /// <summary>Primary media type string from the first Work (e.g. "Epub", "Video").</summary>
    public string? PrimaryMediaType => Works.FirstOrDefault()?.MediaType;

    /// <summary>Best description across all works.</summary>
    public string? Description => Works.Select(w => w.Description).FirstOrDefault(d => !string.IsNullOrEmpty(d));

    /// <summary>Attribution string for the description (e.g. "Wikipedia (CC BY-SA 4.0)").</summary>
    public string? DescriptionSource => Works.Select(w => w.DescriptionSource).FirstOrDefault(s => !string.IsNullOrEmpty(s));

    /// <summary>Best author/creator across all works.</summary>
    public string? Author => Works.Select(w => w.Author).FirstOrDefault(a => !string.IsNullOrEmpty(a));

    /// <summary>Genre tags from the first Work with genre data.</summary>
    public string? Genre => Works.Select(w => w.Genre).FirstOrDefault(g => !string.IsNullOrEmpty(g));

    /// <summary>Series name from the first Work with series data.</summary>
    public string? Series => Works.Select(w => w.Series).FirstOrDefault(s => !string.IsNullOrEmpty(s));

    /// <summary>Rating from the first Work with a rating.</summary>
    public string? Rating => Works.Select(w => w.Rating).FirstOrDefault(r => !string.IsNullOrEmpty(r));

    /// <summary>
    /// Brand hex colour derived from the dominant media type across this Hub's Works.
    /// Driven by <see cref="UniverseMapper.ColourForHub"/> so all colour logic
    /// stays in one place (the mapper) and Hubs are colour-blind by design.
    /// </summary>
    public string DominantHexColor { get; init; } = "#9E9E9E";

    // ── Factory ───────────────────────────────────────────────────────────────

    public static HubViewModel FromApiDto(
        Guid id, Guid? universeId, DateTimeOffset createdAt, IEnumerable<WorkViewModel> works)
    {
        var workList = works.ToList();
        return new()
        {
            Id               = id,
            UniverseId       = universeId,
            CreatedAt        = createdAt,
            Works            = workList,
            DominantHexColor = UniverseMapper.ColourForHub(workList),
        };
    }

    private static string? GetTitle(WorkViewModel w) =>
        w.CanonicalValues.FirstOrDefault(cv => cv.Key == "title")?.Value;
}
