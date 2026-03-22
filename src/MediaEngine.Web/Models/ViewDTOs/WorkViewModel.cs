using MediaEngine.Domain;

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

    /// <summary>
    /// All credited authors/creators. The <c>author</c> canonical value uses
    /// <c>|||</c> as a multi-value separator (pen names first per audit ordering).
    /// </summary>
    public IReadOnlyList<string> Authors
    {
        get
        {
            var raw = Canonical("author") ?? Canonical("creator");
            if (string.IsNullOrWhiteSpace(raw)) return [];
            return raw.Split("|||", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }
    }

    /// <summary>
    /// Primary display author — the first value in <see cref="Authors"/>,
    /// which is always the pen name / canonical credited name when the
    /// author audit has run (pen names sort first).
    /// </summary>
    public string? Author => Authors.FirstOrDefault();
    public string? AuthorQid      => Canonical("author_qid");
    public string? WikidataQid    => Canonical("wikidata_qid");

    /// <summary>
    /// The best human-facing identifier for this work based on its media type.
    /// Books → ISBN, Movies/TV → IMDb, Music → MusicBrainz, fallback → QID.
    /// </summary>
    public string? DisplayIdentifier
    {
        get
        {
            var mt = (MediaType ?? string.Empty).ToLowerInvariant();
            if (mt.Contains("book") || mt.Contains("epub") || mt.Contains("audio"))
                return Canonical("isbn") ?? Canonical("asin") ?? WikidataQid;
            if (mt.Contains("movie") || mt.Contains("tv") || mt.Contains("video"))
                return Canonical("imdb_id") ?? Canonical("tmdb_id") ?? WikidataQid;
            if (mt.Contains("music"))
                return Canonical("musicbrainz_id") ?? WikidataQid;
            if (mt.Contains("comic"))
                return Canonical("comicvine_id") ?? WikidataQid;
            return WikidataQid;
        }
    }

    /// <summary>
    /// Label for the <see cref="DisplayIdentifier"/> (e.g. "ISBN", "IMDb", "QID").
    /// </summary>
    public string DisplayIdentifierLabel
    {
        get
        {
            var mt = (MediaType ?? string.Empty).ToLowerInvariant();
            if (mt.Contains("book") || mt.Contains("epub") || mt.Contains("audio"))
            {
                if (Canonical("isbn") is not null) return "ISBN";
                if (Canonical("asin") is not null) return "ASIN";
                return "QID";
            }
            if (mt.Contains("movie") || mt.Contains("tv") || mt.Contains("video"))
            {
                if (Canonical("imdb_id") is not null) return "IMDb";
                if (Canonical("tmdb_id") is not null) return "TMDB";
                return "QID";
            }
            if (mt.Contains("music")) return Canonical("musicbrainz_id") is not null ? "MusicBrainz" : "QID";
            if (mt.Contains("comic")) return Canonical("comicvine_id") is not null ? "Comic Vine" : "QID";
            return "QID";
        }
    }

    public string? Year           => Canonical("release_year") ?? Canonical("year");
    public string? CoverUrl       => Canonical("cover_url") ?? Canonical("cover");
    public string? HeroUrl        => Canonical("hero_url") ?? Canonical("hero");
    public string? Description       => Canonical("description");
    public string? DescriptionSource => Canonical("description_source");
    public string? Genre          => Canonical("genre");

    /// <summary>
    /// Genre as an array of individual values. Splits <c>|||</c>-separated
    /// or semicolon-separated genre strings into individual entries.
    /// </summary>
    public IReadOnlyList<string> Genres
    {
        get
        {
            var raw = Canonical("genre");
            if (string.IsNullOrWhiteSpace(raw)) return [];
            var sep = raw.Contains("|||", StringComparison.Ordinal) ? "|||" : ";";
            return raw.Split(sep, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }
    }

    /// <summary>
    /// Genre QIDs matching <see cref="Genres"/> by position.
    /// </summary>
    public IReadOnlyList<string> GenreQids
    {
        get
        {
            var raw = Canonical("genre_qid");
            if (string.IsNullOrWhiteSpace(raw)) return [];
            var sep = raw.Contains("|||", StringComparison.Ordinal) ? "|||" : ";";
            return raw.Split(sep, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }
    }
    public string? Narrator       => Canonical("narrator");
    public string? Director       => Canonical("director");
    public string? Series         => Canonical("series");
    public string? SeriesPosition => Canonical("series_position");
    public string? Rating         => Canonical("rating");
    public string? FictionalUniverseQid => Canonical("fictional_universe_qid");
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

    private static HashSet<string> MultiValuedKeys => MetadataFieldConstants.MultiValuedKeys;

    private string? Canonical(string key)
    {
        var raw = CanonicalValues.FirstOrDefault(cv => cv.Key.Equals(key, StringComparison.OrdinalIgnoreCase))?.Value;
        if (raw is not null && raw.Contains("|||", StringComparison.Ordinal) && !MultiValuedKeys.Contains(key))
            return raw.Split("|||", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        return raw;
    }
}

/// <summary>A single scored metadata field on a Work or Edition.</summary>
public sealed class CanonicalValueViewModel
{
    public string          Key          { get; init; } = string.Empty;
    public string          Value        { get; init; } = string.Empty;
    public DateTimeOffset  LastScoredAt { get; init; }
}
