using System.Text;

namespace Tanaste.Providers.Models;

/// <summary>
/// The Master Authority Table: a configurable mapping of Wikidata P-codes to Tanaste claim keys.
///
/// <para>
/// <b>Defaults are compiled-in</b> via <see cref="DefaultMap"/>. Per-instance overrides
/// live in <c>tanaste_master.json → wikidata_property_map</c> and are applied at runtime
/// via <see cref="MergeOverrides"/>. If the JSON is missing or corrupt, the defaults still work.
/// </para>
///
/// <para>
/// <b>Copyright constraint:</b> P18 (Image) is <b>Person-scoped only</b>. Wikimedia Commons
/// headshots of public figures are not copyrighted in the same way as commercial cover art.
/// Media cover art comes exclusively from Apple Books, Audnexus, and TMDB — never from Wikidata.
/// The <see cref="BuildWorkSparqlQuery"/> method deliberately excludes P18.
/// </para>
/// </summary>
public static class WikidataSparqlPropertyMap
{
    // ── Default Map ──────────────────────────────────────────────────────────

    /// <summary>
    /// The exhaustive default property map. Every Wikidata property the Engine tracks
    /// is listed here. Adding a new property is one entry in this dictionary.
    /// </summary>
    public static IReadOnlyDictionary<string, WikidataProperty> DefaultMap { get; } =
        BuildDefaultMap();

    // ── Lookup Helpers ───────────────────────────────────────────────────────

    /// <summary>Look up a property by its Wikidata P-code (e.g. "P179").</summary>
    public static WikidataProperty? GetByPCode(
        string pcode,
        IReadOnlyDictionary<string, WikidataProperty>? effectiveMap = null)
    {
        var map = effectiveMap ?? DefaultMap;
        return map.GetValueOrDefault(pcode);
    }

    /// <summary>Return all properties flagged as external bridge identifiers.</summary>
    public static IReadOnlyList<WikidataProperty> GetBridgeProperties(
        IReadOnlyDictionary<string, WikidataProperty>? effectiveMap = null)
    {
        var map = effectiveMap ?? DefaultMap;
        return map.Values.Where(p => p.IsBridge && p.Enabled).ToList();
    }

    /// <summary>Return all enabled properties for a given entity scope.</summary>
    public static IReadOnlyList<WikidataProperty> GetByScope(
        string scope,
        IReadOnlyDictionary<string, WikidataProperty>? effectiveMap = null)
    {
        var map = effectiveMap ?? DefaultMap;
        return map.Values
            .Where(p => p.Enabled &&
                        (p.EntityScope.Equals(scope, StringComparison.OrdinalIgnoreCase) ||
                         p.EntityScope.Equals("Both", StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    // ── Override Merge ───────────────────────────────────────────────────────

    /// <summary>
    /// Apply JSON overrides on top of the compiled defaults.
    /// Returns a new dictionary; the original <see cref="DefaultMap"/> is never mutated.
    /// </summary>
    /// <param name="overrides">
    /// Override entries from <c>tanaste_master.json → wikidata_property_map</c>.
    /// Each entry targets a P-code and may override claim key, confidence, or enabled state.
    /// </param>
    public static IReadOnlyDictionary<string, WikidataProperty> MergeOverrides(
        IReadOnlyList<WikidataPropertyOverride>? overrides)
    {
        if (overrides is null || overrides.Count == 0)
            return DefaultMap;

        var merged = new Dictionary<string, WikidataProperty>(DefaultMap, StringComparer.OrdinalIgnoreCase);

        foreach (var ov in overrides)
        {
            if (string.IsNullOrWhiteSpace(ov.PCode))
                continue;

            if (merged.TryGetValue(ov.PCode, out var existing))
            {
                // Apply non-null overrides to existing entry
                merged[ov.PCode] = existing with
                {
                    ClaimKey   = ov.ClaimKey   ?? existing.ClaimKey,
                    Confidence = ov.Confidence  ?? existing.Confidence,
                    Enabled    = ov.Enabled     ?? existing.Enabled,
                };
            }
            else if (ov.ClaimKey is not null)
            {
                // User added a brand-new property not in the defaults
                merged[ov.PCode] = new WikidataProperty
                {
                    PCode      = ov.PCode,
                    ClaimKey   = ov.ClaimKey,
                    Category   = "Custom",
                    EntityScope = "Work",
                    Confidence = ov.Confidence ?? 0.8,
                    IsBridge   = false,
                    Enabled    = ov.Enabled ?? true,
                };
            }
        }

        return merged;
    }

    // ── SPARQL Query Builders ────────────────────────────────────────────────

    /// <summary>
    /// Build a SPARQL SELECT query for all enabled Work-scoped properties of a given QID.
    /// <b>Deliberately excludes P18 (Image)</b> — Person-only due to copyright constraints.
    /// </summary>
    public static string BuildWorkSparqlQuery(
        string qid,
        IReadOnlyDictionary<string, WikidataProperty>? effectiveMap = null)
    {
        var props = GetByScope("Work", effectiveMap)
            .Where(p => p.PCode != "P18") // P18 is Person-only (copyright)
            .ToList();

        return BuildSparqlQuery(qid, props);
    }

    /// <summary>
    /// Build a SPARQL SELECT query for all enabled Person-scoped properties of a given QID.
    /// Includes P18 (Image) — headshots of public figures are not copyrighted.
    /// </summary>
    public static string BuildPersonSparqlQuery(
        string qid,
        IReadOnlyDictionary<string, WikidataProperty>? effectiveMap = null)
    {
        var props = GetByScope("Person", effectiveMap);
        return BuildSparqlQuery(qid, props);
    }

    /// <summary>
    /// Build a SPARQL SELECT query to find a QID by a specific bridge property value.
    /// Example: <c>BuildBridgeLookupQuery("P1566", "B00K0OI2DK")</c> → finds the Q-item with that ASIN.
    /// </summary>
    public static string BuildBridgeLookupQuery(string pCode, string value)
    {
        // Escape double quotes in the value
        var escaped = value.Replace("\"", "\\\"");
        return $"SELECT ?item WHERE {{ ?item wdt:{pCode} \"{escaped}\" . }} LIMIT 1";
    }

    // ── Private Helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Builds the Wikidata SPARQL query for a given QID and set of properties.
    /// Uses OPTIONAL clauses so missing properties don't exclude the entity.
    /// For entity-valued properties, fetches rdfs:label in English.
    /// </summary>
    private static string BuildSparqlQuery(string qid, IReadOnlyList<WikidataProperty> props)
    {
        if (props.Count == 0)
            return string.Empty;

        var sb = new StringBuilder(2048);

        // SELECT clause — one variable per property
        sb.Append("SELECT");
        foreach (var p in props)
        {
            var varName = p.PCode.ToLowerInvariant(); // e.g. ?p179
            sb.Append(" ?").Append(varName);
            // Entity-valued properties also get a label variable
            if (IsEntityValued(p.PCode))
                sb.Append(" ?").Append(varName).Append("Label");
        }
        sb.AppendLine();

        // WHERE clause
        sb.AppendLine("WHERE {");

        foreach (var p in props)
        {
            var varName = p.PCode.ToLowerInvariant();
            sb.Append("  OPTIONAL { wd:").Append(qid)
              .Append(" wdt:").Append(p.PCode)
              .Append(" ?").Append(varName).Append(" . ");

            if (IsEntityValued(p.PCode))
            {
                sb.Append("?").Append(varName)
                  .Append(" rdfs:label ?").Append(varName).Append("Label . ")
                  .Append("FILTER(LANG(?").Append(varName).Append("Label) = \"en\") ");
            }

            sb.AppendLine("}");
        }

        sb.AppendLine("}");
        sb.AppendLine("LIMIT 1");

        return sb.ToString();
    }

    /// <summary>
    /// Properties whose Wikidata values are Q-items (entities) rather than literals.
    /// These need rdfs:label fetching to get a human-readable name.
    /// </summary>
    private static bool IsEntityValued(string pCode) => pCode switch
    {
        "P31"   => true,  // instance of
        "P179"  => true,  // series
        "P8345" => true,  // franchise
        "P155"  => true,  // preceded by
        "P156"  => true,  // followed by
        "P50"   => true,  // author
        "P110"  => true,  // illustrator
        "P57"   => true,  // director
        "P161"  => true,  // cast member
        "P987"  => true,  // narrator
        "P725"  => true,  // voice actor
        "P58"   => true,  // screenwriter
        "P86"   => true,  // composer
        "P800"  => true,  // notable work
        "P106"  => true,  // occupation
        "P840"  => true,  // narrative location
        "P674"  => true,  // characters
        "P921"  => true,  // main subject
        "P1434" => true,  // fictional universe
        "P144"  => true,  // based on
        "P4584" => true,  // first appearance
        "P407"  => true,  // language of work
        _ => false,
    };

    // ── Default Map Builder ──────────────────────────────────────────────────

    private static Dictionary<string, WikidataProperty> BuildDefaultMap()
    {
        var map = new Dictionary<string, WikidataProperty>(64, StringComparer.OrdinalIgnoreCase);

        void Add(string pCode, string claimKey, string category,
                 string scope = "Work", double confidence = 0.9, bool bridge = false)
        {
            map[pCode] = new WikidataProperty
            {
                PCode       = pCode,
                ClaimKey    = claimKey,
                Category    = category,
                EntityScope = scope,
                Confidence  = confidence,
                IsBridge    = bridge,
            };
        }

        // ── Core Identity (Work-scoped) ──────────────────────────────────
        Add("P31",   "instance_of",     "Core Identity");
        Add("P1476", "title",           "Core Identity");
        Add("P179",  "series",          "Core Identity");
        Add("P1545", "series_position", "Core Identity");
        Add("P8345", "franchise",       "Core Identity");
        Add("P155",  "preceded_by",     "Core Identity", confidence: 0.8);
        Add("P156",  "followed_by",     "Core Identity", confidence: 0.8);
        Add("P577",  "year",            "Core Identity");

        // ── People — Work-scoped (link Work → Person QID) ───────────────
        Add("P50",  "author",       "People");
        Add("P110", "illustrator",  "People");
        Add("P57",  "director",     "People");
        Add("P161", "cast_member",  "People");
        Add("P987", "narrator",     "People");
        Add("P725", "voice_actor",  "People");
        Add("P58",  "screenwriter", "People");
        Add("P86",  "composer",     "People");

        // ── People — Person-scoped (enrich the Person entity itself) ────
        // P18 (Image): Person-only — copyright constraint.
        // Wikimedia Commons headshots of public figures are not copyrighted.
        // Media cover art comes exclusively from Apple Books, Audnexus, and TMDB.
        Add("P800", "notable_work", "People", scope: "Person", confidence: 0.85);
        Add("P18",  "headshot_url", "People", scope: "Person");
        Add("P106", "occupation",   "People", scope: "Person", confidence: 0.85);

        // ── Lore & Narrative (Work-scoped) ───────────────────────────────
        Add("P840",  "narrative_location", "Lore & Narrative", confidence: 0.8);
        Add("P674",  "characters",         "Lore & Narrative", confidence: 0.8);
        Add("P921",  "main_subject",       "Lore & Narrative", confidence: 0.8);
        Add("P1434", "fictional_universe", "Lore & Narrative", confidence: 0.8);
        Add("P144",  "based_on",           "Lore & Narrative", confidence: 0.8);
        Add("P4584", "first_appearance",   "Lore & Narrative", confidence: 0.8);

        // ── Bridges: Books (Work-scoped) ─────────────────────────────────
        Add("P3861", "apple_books_id", "Bridges: Books", confidence: 1.0, bridge: true);
        Add("P212",  "isbn",           "Bridges: Books", confidence: 1.0, bridge: true);
        Add("P1566", "asin",           "Bridges: Books", confidence: 1.0, bridge: true);
        Add("P2969", "goodreads_id",   "Bridges: Books", confidence: 1.0, bridge: true);
        Add("P244",  "loc_id",         "Bridges: Books", confidence: 1.0, bridge: true);

        // ── Bridges: Movies/TV (Work-scoped) ─────────────────────────────
        Add("P4947", "tmdb_id",       "Bridges: Movies/TV", confidence: 1.0, bridge: true);
        Add("P345",  "imdb_id",       "Bridges: Movies/TV", confidence: 1.0, bridge: true);
        Add("P9385", "justwatch_id",  "Bridges: Movies/TV", confidence: 1.0, bridge: true);
        Add("P1712", "metacritic_id", "Bridges: Movies/TV", confidence: 1.0, bridge: true);
        Add("P6127", "letterboxd_id", "Bridges: Movies/TV", confidence: 1.0, bridge: true);
        Add("P2638", "tvcom_id",      "Bridges: Movies/TV", confidence: 1.0, bridge: true);

        // ── Bridges: Comics/Anime (Work-scoped) ─────────────────────────
        Add("P3589",  "gcd_series_id", "Bridges: Comics/Anime", confidence: 1.0, bridge: true);
        Add("P11308", "gcd_issue_id",  "Bridges: Comics/Anime", confidence: 1.0, bridge: true);
        Add("P5905",  "comicvine_id",  "Bridges: Comics/Anime", confidence: 1.0, bridge: true);
        Add("P4084",  "mal_anime_id",  "Bridges: Comics/Anime", confidence: 1.0, bridge: true);
        Add("P4087",  "mal_manga_id",  "Bridges: Comics/Anime", confidence: 1.0, bridge: true);

        // ── Bridges: Music/Audio (Work-scoped) ──────────────────────────
        Add("P434",  "musicbrainz_id", "Bridges: Music/Audio", confidence: 1.0, bridge: true);
        Add("P1902", "spotify_id",     "Bridges: Music/Audio", confidence: 1.0, bridge: true);
        Add("P1953", "discogs_id",     "Bridges: Music/Audio", confidence: 1.0, bridge: true);
        Add("P3398", "audible_id",     "Bridges: Music/Audio", confidence: 1.0, bridge: true);

        // ── Social Pivot (Person-scoped) ─────────────────────────────────
        Add("P2003", "instagram", "Social Pivot", scope: "Person", confidence: 1.0, bridge: true);
        Add("P2002", "twitter",   "Social Pivot", scope: "Person", confidence: 1.0, bridge: true);
        Add("P7085", "tiktok",    "Social Pivot", scope: "Person", confidence: 1.0, bridge: true);
        Add("P4033", "mastodon",  "Social Pivot", scope: "Person", confidence: 1.0, bridge: true);
        Add("P856",  "website",   "Social Pivot", scope: "Person", confidence: 1.0, bridge: true);

        return map;
    }
}

/// <summary>
/// A user-defined override for a single Wikidata property in <c>tanaste_master.json</c>.
/// Only non-null fields replace the compiled default.
/// </summary>
public sealed class WikidataPropertyOverride
{
    /// <summary>The Wikidata property code to override, e.g. <c>"P179"</c>.</summary>
    public string PCode { get; set; } = string.Empty;

    /// <summary>Override the claim key. <c>null</c> = keep default.</summary>
    public string? ClaimKey { get; set; }

    /// <summary>Override the confidence. <c>null</c> = keep default.</summary>
    public double? Confidence { get; set; }

    /// <summary>Disable this property entirely. <c>null</c> = keep default (<c>true</c>).</summary>
    public bool? Enabled { get; set; }
}
