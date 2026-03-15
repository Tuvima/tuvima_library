namespace MediaEngine.Ingestion;

/// <summary>
/// Static helpers that gate organization and scoring decisions.
/// Keeps policy logic in one place so both <see cref="IngestionEngine"/>
/// and <see cref="AutoOrganizeService"/> apply the same rules.
/// </summary>
internal static class MetadataGuards
{
    private static readonly HashSet<string> PlaceholderTitles =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "unknown", "untitled", "unnamed", "no title", "title", "",
        };

    /// <summary>
    /// Returns <c>true</c> when <paramref name="title"/> is null, blank,
    /// or one of the well-known placeholder strings that processors emit
    /// for files with no real title metadata.
    /// </summary>
    public static bool IsPlaceholderTitle(string? title)
        => string.IsNullOrWhiteSpace(title) || PlaceholderTitles.Contains(title.Trim());

    /// <summary>
    /// Returns <c>true</c> when the claim set contains at least one bridge
    /// identifier (isbn, asin, wikidata_qid) that proves the file's identity
    /// independently of its title.
    /// </summary>
    public static bool HasBridgeId(IReadOnlyDictionary<string, string> canonicals)
    {
        return canonicals.TryGetValue("isbn", out var isbn) && !string.IsNullOrWhiteSpace(isbn)
            || canonicals.TryGetValue("asin", out var asin) && !string.IsNullOrWhiteSpace(asin)
            || canonicals.TryGetValue("wikidata_qid", out var qid) && !string.IsNullOrWhiteSpace(qid);
    }
}
