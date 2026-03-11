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
    public string? Description       => Canonical("description");
    public string? DescriptionSource => Canonical("description_source");
    public string? Genre          => Canonical("genre");
    public string? Narrator       => Canonical("narrator");
    public string? Director       => Canonical("director");
    public string? Series         => Canonical("series");
    public string? SeriesPosition => Canonical("series_position");
    public string? Rating         => Canonical("rating");
    public int?    WordCount       => int.TryParse(Canonical("word_count"), out var wc) ? wc : null;

    public string? ReadingTimeDisplay
    {
        get
        {
            if (WordCount is not { } wc || wc <= 0) return null;
            var minutes = wc / 250;
            if (minutes < 60) return $"{wc:N0} words \u2022 {minutes} min read";
            var hours = minutes / 60;
            var rem   = minutes % 60;
            var timeStr = rem > 0 ? $"{hours}h {rem}m" : $"{hours}h";
            return $"{wc:N0} words \u2022 {timeStr} reading time";
        }
    }

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
