using MediaEngine.Web.Services.Integration;

namespace MediaEngine.Web.Models.ViewDTOs;

/// <summary>
/// UI representation of a Collection (media collection).
/// Maps from the API's CollectionDto; adds display-friendly helper properties.
/// For Parent Collections (Universes), override properties supply aggregated data
/// since parent collections have no direct Works.
/// </summary>
public sealed class CollectionViewModel
{
    public Guid                Id               { get; init; }
    public Guid?               UniverseId       { get; init; }
    public DateTimeOffset      CreatedAt        { get; init; }
    public List<WorkViewModel> Works            { get; init; } = [];

    // ── Parent Collection / franchise hierarchy ────────────────────────────────────

    /// <summary>ID of the franchise-level Parent Collection, if any.</summary>
    public Guid? ParentCollectionId { get; init; }

    /// <summary>Display name of the Parent Collection (for breadcrumb rendering).</summary>
    public string? ParentCollectionName { get; init; }

    /// <summary>Number of child Collections under this Collection (when acting as a Parent Collection).</summary>
    public int ChildCollectionCount { get; init; }

    /// <summary>True when this Collection is a Parent Collection (has children).</summary>
    public bool IsParentCollection => ChildCollectionCount > 0;

    // ── Override properties for Parent Collections ─────────────────────────────────
    // Parent collections aggregate data from children; these overrides bypass the
    // work-derived defaults when set.

    /// <summary>Override description for parent collections (set from the collection's own description field).</summary>
    public string? DescriptionOverride { get; init; }

    /// <summary>Override work count for parent collections (total works across child collections).</summary>
    public int? WorkCountOverride { get; init; }

    /// <summary>Override media types string for parent collections (aggregated from child collections).</summary>
    public string? MediaTypesOverride { get; init; }

    /// <summary>Override fictional universe QID for parent collections (from the collection's own wikidata_qid).</summary>
    public string? FictionalUniverseQidOverride { get; init; }

    // ── Display helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Collection-level display name set by the Engine at organise time (e.g. the
    /// work title or series name stored in <c>collections.display_name</c>).
    /// Falls back to the first Work's title when the Engine hasn't set one yet.
    /// </summary>
    public string? CollectionDisplayName { get; init; }

    /// <summary>Best title: Hub's own name first, then first Work's title, then short ID.</summary>
    public string DisplayName =>
        (!string.IsNullOrWhiteSpace(CollectionDisplayName) ? CollectionDisplayName : null)
        ?? Works.Select(GetTitle).FirstOrDefault(t => !string.IsNullOrEmpty(t))
        ?? $"Collection {Id:N}"[..12];

    public int    WorkCount  => WorkCountOverride ?? Works.Count;
    public string MediaTypes => MediaTypesOverride ?? string.Join(", ", Works.Select(w => w.MediaType).Distinct());
    public bool   HasWorks   => Works.Count > 0 || (WorkCountOverride ?? 0) > 0;

    /// <summary>Cover art URL from the first Work's canonical "cover" value (external provider URL).</summary>
    public string? CoverUrl => Works.Select(w => w.CoverUrl).FirstOrDefault(u => !string.IsNullOrEmpty(u));

    /// <summary>Pre-rendered hero banner URL from the first Work's canonical "hero" value.</summary>
    public string? HeroUrl => Works.Select(w => w.HeroUrl).FirstOrDefault(u => !string.IsNullOrEmpty(u));

    /// <summary>Release year from the first Work with a year value.</summary>
    public string? Year => Works.Select(w => w.Year).FirstOrDefault(y => !string.IsNullOrEmpty(y));

    /// <summary>
    /// All distinct media types across the Collection's Works (e.g. "Epub, Audiobooks").
    /// Use this for display badges. For single-work Collections this will be one value.
    /// </summary>
    public string? PrimaryMediaType =>
        Works.Select(w => w.MediaType)
             .Where(t => !string.IsNullOrWhiteSpace(t))
             .Distinct()
             .FirstOrDefault();

    /// <summary>Best description across all works, or override for parent collections.</summary>
    public string? Description =>
        DescriptionOverride
        ?? Works.Select(w => w.Description).FirstOrDefault(d => !string.IsNullOrEmpty(d));

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
    public string? FictionalUniverseQid =>
        FictionalUniverseQidOverride
        ?? Works.Select(w => w.FictionalUniverseQid).FirstOrDefault(q => !string.IsNullOrEmpty(q));

    /// <summary>Best human-facing identifier from the first Work with one.</summary>
    public string? DisplayIdentifier =>
        Works.Select(w => w.DisplayIdentifier).FirstOrDefault(id => !string.IsNullOrEmpty(id));

    /// <summary>Label for <see cref="DisplayIdentifier"/>.</summary>
    public string DisplayIdentifierLabel =>
        Works.Select(w => w.DisplayIdentifierLabel).FirstOrDefault(l => l != "QID") ?? "QID";

    /// <summary>
    /// Brand hex colour derived from the dominant media type across this Collection's Works.
    /// Driven by <see cref="UniverseMapper.ColourForCollection"/> so all colour logic
    /// stays in one place (the mapper) and Collections are colour-blind by design.
    /// </summary>
    public string DominantHexColor { get; init; } = "#9E9E9E";

    // ── Factory ───────────────────────────────────────────────────────────────

    public static CollectionViewModel FromApiDto(
        Guid id, Guid? universeId, DateTimeOffset createdAt, IEnumerable<WorkViewModel> works,
        string? displayName = null, Guid? parentCollectionId = null, string? parentCollectionName = null, int childCollectionCount = 0)
    {
        var workList = works.ToList();
        return new()
        {
            Id               = id,
            UniverseId       = universeId,
            CreatedAt        = createdAt,
            Works            = workList,
            CollectionDisplayName   = displayName,
            DominantHexColor = UniverseMapper.ColourForCollection(workList),
            ParentCollectionId      = parentCollectionId,
            ParentCollectionName    = parentCollectionName,
            ChildCollectionCount    = childCollectionCount,
        };
    }

    /// <summary>
    /// Factory for Parent Collection (Universe) view models returned by GET /collections/parents.
    /// Parent collections have no direct works — aggregated data is supplied via overrides.
    /// </summary>
    public static CollectionViewModel FromParentCollection(
        Guid id, Guid? universeId, DateTimeOffset createdAt,
        string? displayName, string? description, string? wikidataQid,
        int childCollectionCount, string? mediaTypes, int totalWorks)
    {
        return new()
        {
            Id                            = id,
            UniverseId                    = universeId,
            CreatedAt                     = createdAt,
            Works                         = [],
            CollectionDisplayName                = displayName,
            DominantHexColor              = "#A78BFA",  // Purple accent for Universe-level collections
            ChildCollectionCount                 = childCollectionCount,
            DescriptionOverride           = description,
            WorkCountOverride             = totalWorks,
            MediaTypesOverride            = mediaTypes,
            FictionalUniverseQidOverride  = wikidataQid,
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
