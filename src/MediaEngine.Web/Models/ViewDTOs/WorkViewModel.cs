namespace MediaEngine.Web.Models.ViewDTOs;

/// <summary>UI representation of a Work (individual media item).</summary>
public sealed class WorkViewModel
{
    public Guid                         Id              { get; init; }
    public Guid?                        HubId           { get; init; }
    public string                       MediaType       { get; init; } = string.Empty;
    public int?                         SequenceIndex   { get; init; }
    public List<CanonicalValueViewModel> CanonicalValues { get; init; } = [];

    // ── Display helpers ───────────────────────────────────────────────────────

    public string  Title          => Canonical("title") ?? $"Untitled ({MediaType})";
    public string? Author         => Canonical("author") ?? Canonical("creator");
    public string? Year           => Canonical("release_year") ?? Canonical("year");
    public string? CoverUrl       => Canonical("cover");
    public string? HeroUrl        => Canonical("hero");
    public string? Description    => Canonical("description");
    public string? Genre          => Canonical("genre");
    public string? Narrator       => Canonical("narrator");
    public string? Director       => Canonical("director");
    public string? Series         => Canonical("series");
    public string? SeriesPosition => Canonical("series_position");
    public string? Rating         => Canonical("rating");

    private string? Canonical(string key) =>
        CanonicalValues.FirstOrDefault(cv => cv.Key.Equals(key, StringComparison.OrdinalIgnoreCase))?.Value;
}

/// <summary>A single scored metadata field on a Work or Edition.</summary>
public sealed class CanonicalValueViewModel
{
    public string          Key          { get; init; } = string.Empty;
    public string          Value        { get; init; } = string.Empty;
    public DateTimeOffset  LastScoredAt { get; init; }
}
