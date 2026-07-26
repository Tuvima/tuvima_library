using System.Text.RegularExpressions;
using MediaEngine.Domain;
using MediaEngine.Domain.Enums;

namespace MediaEngine.Providers.Services;

/// <summary>
/// Shared scoring/matching helpers consolidated out of duplicated private methods across
/// <see cref="RetailCandidateScorer"/>, <see cref="RetailMatchScoringService"/>,
/// <c>RetailMatchWorker</c>, <see cref="SearchService"/>, <c>ReconciliationAdapter</c>,
/// <see cref="CollectionAssignmentService"/>, <c>PersonEnrichmentWorker</c>, and
/// <see cref="RecursiveIdentityService"/>.
///
/// This class intentionally does NOT re-implement text-similarity primitives
/// (diacritic stripping, word-overlap, comparable-text normalization) — those already have
/// a single canonical home in <see cref="RetailTextSimilarity"/>. Anything here that needs
/// text-similarity behavior delegates to that class instead of duplicating it again.
///
/// This packet only builds the shared static and pins its behavior with tests. Call sites are
/// migrated in a later wave — see each method's remarks for which original private method(s)
/// it replaces and how any behavioral differences were resolved.
/// </summary>
public static class RetailHints
{
    // ── Year hints ──────────────────────────────────────────────────────────

    /// <summary>
    /// Extracts a 4-digit year token from free-form text (e.g. "2019-03-01", "(2019)").
    /// Replaces the byte-identical private methods
    /// <c>RetailCandidateScorer.NormalizeYearValue</c>, <c>RetailMatchScoringService.NormalizeYear</c>,
    /// and <c>RetailMatchWorker.NormalizeYearValue</c> — all three used the exact same
    /// <c>\b\d{4}\b</c> regex, so this is a pure name unification, no behavior change.
    /// </summary>
    public static string? NormalizeYear(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var match = Regex.Match(value, @"\b\d{4}\b");
        return match.Success ? match.Value : null;
    }

    /// <summary>
    /// Resolves the best available year hint from file metadata, trying
    /// <c>year</c> → <c>release_year</c> → <c>date</c> → <c>release_date</c> in order and
    /// normalizing the first non-blank match via <see cref="NormalizeYear"/>.
    /// Replaces the byte-identical private methods
    /// <c>RetailMatchScoringService.GetYearHint</c> and <c>RetailCandidateScorer</c> /
    /// <c>RetailMatchWorker</c>'s <c>GetPrimaryYearHint</c> — all three used the same
    /// fallback chain, so this is a pure name unification, no behavior change.
    /// </summary>
    public static string? GetYearHint(IReadOnlyDictionary<string, string> fileHints)
    {
        return NormalizeYear(
            fileHints.GetValueOrDefault(MetadataFieldConstants.Year)
            ?? fileHints.GetValueOrDefault("release_year")
            ?? fileHints.GetValueOrDefault("date")
            ?? fileHints.GetValueOrDefault("release_date"));
    }

    // ── Creator hints ───────────────────────────────────────────────────────

    /// <summary>
    /// Resolves the best available creator/author hint from file metadata using a flat,
    /// media-type-agnostic fallback chain: author → artist → composer → director → writer →
    /// show name → series.
    /// Replaces the byte-identical private method <c>GetPrimaryCreatorHint</c> found in both
    /// <c>RetailCandidateScorer</c> and <c>RetailMatchWorker</c>.
    /// </summary>
    /// <remarks>
    /// Deliberately kept as a separate overload rather than folded into
    /// <see cref="GetCreatorHint(IReadOnlyDictionary{string, string}, MediaType)"/>: that
    /// media-type-aware variant's default case (from <c>RetailMatchScoringService</c>) does
    /// NOT include the trailing show-name/series fallback, so unifying the two under one
    /// signature would have silently dropped a fallback for callers that don't pass a
    /// <see cref="MediaType"/>. Keeping both preserves each call site's existing behavior
    /// exactly; wave 2 picks whichever overload matches what the call site did before.
    /// </remarks>
    public static string? GetCreatorHint(IReadOnlyDictionary<string, string> fileHints)
    {
        return fileHints.GetValueOrDefault(MetadataFieldConstants.Author)
            ?? fileHints.GetValueOrDefault(MetadataFieldConstants.Artist)
            ?? fileHints.GetValueOrDefault(MetadataFieldConstants.Composer)
            ?? fileHints.GetValueOrDefault(MetadataFieldConstants.Director)
            ?? fileHints.GetValueOrDefault("writer")
            ?? fileHints.GetValueOrDefault(MetadataFieldConstants.ShowName)
            ?? fileHints.GetValueOrDefault(MetadataFieldConstants.Series);
    }

    /// <summary>
    /// Resolves the best available creator/author hint from file metadata using a
    /// media-type-aware fallback chain (music favors artist/composer, TV favors show
    /// name/series, comics favor writer/illustrator, etc.).
    /// Replaces the private method <c>RetailMatchScoringService.GetCreatorHint</c> — per
    /// packet instructions, this is the "unify on the RetailMatchScoringService variant"
    /// choice for the media-type-aware shape. See <see cref="GetCreatorHint(IReadOnlyDictionary{string, string})"/>
    /// for the flat fallback used by callers that don't have a <see cref="MediaType"/>.
    /// </summary>
    public static string? GetCreatorHint(IReadOnlyDictionary<string, string> fileHints, MediaType mediaType)
    {
        return mediaType switch
        {
            MediaType.Music => fileHints.GetValueOrDefault(MetadataFieldConstants.Artist)
                ?? fileHints.GetValueOrDefault(MetadataFieldConstants.Composer)
                ?? fileHints.GetValueOrDefault(MetadataFieldConstants.Author),

            MediaType.TV => fileHints.GetValueOrDefault(MetadataFieldConstants.Author)
                ?? fileHints.GetValueOrDefault(MetadataFieldConstants.ShowName)
                ?? fileHints.GetValueOrDefault(MetadataFieldConstants.Series)
                ?? fileHints.GetValueOrDefault(MetadataFieldConstants.Director)
                ?? fileHints.GetValueOrDefault("writer"),

            MediaType.Movies => fileHints.GetValueOrDefault(MetadataFieldConstants.Author)
                ?? fileHints.GetValueOrDefault(MetadataFieldConstants.Director)
                ?? fileHints.GetValueOrDefault("writer"),

            MediaType.Comics => fileHints.GetValueOrDefault(MetadataFieldConstants.Author)
                ?? fileHints.GetValueOrDefault("writer")
                ?? fileHints.GetValueOrDefault(MetadataFieldConstants.Illustrator),

            _ => fileHints.GetValueOrDefault(MetadataFieldConstants.Author)
                ?? fileHints.GetValueOrDefault(MetadataFieldConstants.Artist)
                ?? fileHints.GetValueOrDefault(MetadataFieldConstants.Composer)
                ?? fileHints.GetValueOrDefault(MetadataFieldConstants.Director)
                ?? fileHints.GetValueOrDefault("writer"),
        };
    }

    // ── Ordinal / issue-number comparison ───────────────────────────────────

    /// <summary>
    /// Compares two ordinal-ish strings (issue numbers, series positions) for equivalence,
    /// first by parsing leading digits numerically (so "issue 03" == "3"), falling back to
    /// zero-trimmed and plain string equality.
    /// Replaces the byte-identical private method <c>AreEquivalentOrdinals</c> found in both
    /// <c>RetailMatchScoringService</c> and <see cref="SearchService"/>.
    /// </summary>
    public static bool AreEquivalentOrdinals(string left, string right)
    {
        if (int.TryParse(ExtractLeadingDigits(left), out var leftNumber)
            && int.TryParse(ExtractLeadingDigits(right), out var rightNumber))
        {
            return leftNumber == rightNumber;
        }

        return string.Equals(left.TrimStart('0'), right.TrimStart('0'), StringComparison.OrdinalIgnoreCase)
            || string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Extracts the leading run of digits from a string (ignoring leading non-digit
    /// characters and leading zeros), e.g. "No. 007" → "7", "issue" → "".
    /// Replaces the byte-identical private method <c>ExtractLeadingDigits</c> found in both
    /// <c>RetailMatchScoringService</c> and <see cref="SearchService"/>.
    /// </summary>
    public static string ExtractLeadingDigits(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var match = Regex.Match(value.Trim(), @"^\D*0*(\d+)");
        return match.Success ? match.Groups[1].Value : string.Empty;
    }

    // ── Author list splitting ───────────────────────────────────────────────

    /// <summary>
    /// Splits a multi-author/creator string on common separators: " &amp; ", " and " (either
    /// case), and ", ". Returns individual names, trimmed and non-empty.
    /// Replaces the byte-identical private method <c>SplitAuthors</c> found in both
    /// <c>RetailMatchScoringService</c> and <c>ReconciliationAdapter</c>.
    /// </summary>
    public static List<string> SplitAuthors(string authors)
    {
        var parts = Regex.Split(
            authors,
            @"\s+&\s+|\s+and\s+|,\s*",
            RegexOptions.IgnoreCase);

        return parts
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .ToList();
    }

    // ── QID normalization ───────────────────────────────────────────────────

    /// <summary>
    /// Normalizes a raw Wikidata QID value: strips any leading URI path segment (text before
    /// the last '/'), strips any trailing "::label" suffix, trims whitespace, and returns null
    /// unless the result looks like a QID (starts with 'Q').
    /// Replaces two related private methods that behaved slightly differently:
    /// <c>PersonEnrichmentWorker.NormalizeEntityQid(string? raw)</c> already had this exact
    /// shape (null-safe, trims, validates the 'Q' prefix) and is a strict superset of
    /// <c>CollectionAssignmentService.NormalizeQid(string value)</c>, which took a non-null
    /// string, didn't trim, and didn't validate the result actually looked like a QID (its
    /// call site wrapped the result in a separate <c>IsQidLike</c> check instead). Per packet
    /// instructions, the superset variant wins.
    /// </summary>
    public static string? NormalizeQid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var stripped = value.Contains('/') ? value.Split('/')[^1] : value;
        stripped = stripped.Split("::", 2)[0].Trim();
        return stripped.StartsWith('Q') ? stripped : null;
    }

    // ── Person name normalization ────────────────────────────────────────────
    //
    // NOTE ON DEVIATION: the packet spec listed PersonEnrichmentWorker's and
    // RecursiveIdentityService's "NormalizePersonName" as a duplicate pair to unify. On
    // inspection they are NOT duplicates — they share a name but perform entirely different
    // transforms:
    //   - PersonEnrichmentWorker.NormalizePersonName collapses whitespace and upper-cases a
    //     name to build a dictionary lookup key (e.g. "Actor::JOHN SMITH").
    //   - RecursiveIdentityService.NormalizePersonName reverses a "Last, First" bibliographic
    //     name into "First Last" display order, and leaves anything else unchanged.
    // Forcing these into one method under one name would have silently changed behavior at
    // whichever call site lost its actual logic. They are kept as two distinctly named
    // methods instead; wave 2 should point each call site at the one matching its original
    // intent, not to a merged "NormalizePersonName".

    /// <summary>
    /// Builds a case-insensitive, whitespace-collapsed lookup key for a person name (e.g. for
    /// dictionary keys like "Actor::JOHN SMITH"). Originally
    /// <c>PersonEnrichmentWorker.NormalizePersonName</c>.
    /// </summary>
    public static string NormalizePersonNameKey(string name)
        => string.Join(' ', name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();

    /// <summary>
    /// Normalizes a bibliographic "Last, First" name to "First Last" display order. Names
    /// with zero or multiple commas, or a malformed comma placement, are returned trimmed but
    /// otherwise unchanged. Originally <c>RecursiveIdentityService.NormalizePersonName</c>.
    /// </summary>
    public static string NormalizeBibliographicPersonName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return name;

        var trimmed = name.Trim();

        // Only normalize if there's exactly one comma.
        var commaIndex = trimmed.IndexOf(',');
        if (commaIndex < 0 || commaIndex != trimmed.LastIndexOf(','))
            return trimmed;

        var last = trimmed[..commaIndex].Trim();
        var first = trimmed[(commaIndex + 1)..].Trim();

        if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(last))
            return trimmed;

        return $"{first} {last}";
    }
}
