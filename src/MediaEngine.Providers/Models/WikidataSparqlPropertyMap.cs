using System.Text;

namespace MediaEngine.Providers.Models;

/// <summary>
/// The Master Authority Table: a configurable mapping of Wikidata P-codes to claim keys.
///
/// <para>
/// <b>Defaults are compiled-in</b> via <see cref="DefaultMap"/>. Per-instance overrides
/// live in the universe config (<c>config/universe/wikidata.json</c>) and are applied at runtime
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

    /// <summary>
    /// Return all enabled properties for a given entity scope.
    /// Supports comma-separated scopes in <see cref="WikidataProperty.EntityScope"/>
    /// (e.g. <c>"Work,Character,Location,Organization"</c>).
    /// <c>"Both"</c> remains a legacy wildcard matching any scope.
    /// </summary>
    public static IReadOnlyList<WikidataProperty> GetByScope(
        string scope,
        IReadOnlyDictionary<string, WikidataProperty>? effectiveMap = null)
    {
        var map = effectiveMap ?? DefaultMap;
        return map.Values
            .Where(p => p.Enabled && ScopeMatches(p.EntityScope, scope))
            .ToList();
    }

    /// <summary>
    /// Check whether a property's scope declaration matches the requested scope.
    /// Supports comma-separated scopes and the legacy <c>"Both"</c> wildcard.
    /// </summary>
    private static bool ScopeMatches(string propertyScope, string requestedScope)
    {
        if (propertyScope.Equals("Both", StringComparison.OrdinalIgnoreCase))
            return true;

        // Fast path: no comma means single scope — direct comparison.
        if (!propertyScope.Contains(','))
            return propertyScope.Equals(requestedScope, StringComparison.OrdinalIgnoreCase);

        // Comma-separated: split and check each segment.
        foreach (var segment in propertyScope.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment.Equals(requestedScope, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    // ── Override Merge ───────────────────────────────────────────────────────

    /// <summary>
    /// Apply JSON overrides on top of the compiled defaults.
    /// Returns a new dictionary; the original <see cref="DefaultMap"/> is never mutated.
    /// </summary>
    /// <param name="overrides">
    /// Override entries from the legacy manifest's <c>wikidata_property_map</c>.
    /// Each entry targets a P-code and may override claim key, confidence, or enabled state.
    /// </param>
    [Obsolete("Use universe configuration (config/universe/wikidata.json) instead. " +
              "This method remains for legacy migration compatibility.")]
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

    // ── Universe Config Export ─────────────────────────────────────────────

    /// <summary>
    /// Exports the compiled <see cref="DefaultMap"/> as a <see cref="UniverseConfiguration"/>
    /// suitable for writing to <c>config/universe/wikidata.json</c>.
    ///
    /// Used by the configuration migration path and for generating default universe files.
    /// </summary>
    public static UniverseConfiguration ExportAsUniverseConfiguration()
    {
        var propertyMap = new Dictionary<string, WikidataPropertyConfig>(
            DefaultMap.Count, StringComparer.OrdinalIgnoreCase);

        foreach (var (pCode, prop) in DefaultMap)
        {
            propertyMap[pCode] = new WikidataPropertyConfig
            {
                ClaimKey          = prop.ClaimKey,
                Category          = prop.Category,
                EntityScope       = prop.EntityScope,
                Confidence        = prop.Confidence,
                IsBridge          = prop.IsBridge,
                Enabled           = prop.Enabled,
                IsEntityValued    = prop.IsEntityValued,
                ValueTransform    = prop.ValueTransform,
                IsMultiValued     = prop.IsMultiValued,
                IsMonolingualText = prop.IsMonolingualText,
            };
        }

        return new UniverseConfiguration
        {
            ProviderName = "wikidata",
            PropertyMap  = propertyMap,

            // Default bridge lookup priority — ISBN first (definitive match).
            BridgeLookupPriority =
            [
                new() { PCode = "P212",  RequestField = "isbn"                        },
                new() { PCode = "P648",  RequestField = "openlibrary_id"              },
                new() { PCode = "P3861", RequestField = "apple_books_id"              },
                new() { PCode = "P3398", RequestField = "audible_id"                  },
                new() { PCode = "P4947", RequestField = "tmdb_id"                     },
                new() { PCode = "P4835", RequestField = "tvdb_id"                     },
                new() { PCode = "P345",  RequestField = "imdb_id"                     },
                new() { PCode = "P1566", RequestField = "asin"                        },
                new() { PCode = "P11736", RequestField = "anilist_id"                 },
                new() { PCode = "P982",  RequestField = "musicbrainz_release_group_id"},
                new() { PCode = "P5792", RequestField = "igdb_id"                     },
                new() { PCode = "P9968", RequestField = "rawg_id"                     },
                new() { PCode = "P1735", RequestField = "steam_appid"                 },
            ],

            // Copyright constraint: P18 (Image) excluded from Work, Character, Location, Organization scopes.
            // P18 is Person-only — headshots of public figures. Entity images resolved via performer QIDs.
            ScopeExclusions = new()
            {
                ["Work"]         = ["P18"],
                ["Character"]    = ["P18"],
                ["Location"]     = ["P18"],
                ["Organization"] = ["P18"],
            },

            CommonsUrlTemplate =
                "https://commons.wikimedia.org/wiki/Special:FilePath/{0}?width=300",
        };
    }

    /// <summary>
    /// Merges a <see cref="UniverseConfiguration"/> property map on top of the
    /// compiled <see cref="DefaultMap"/>. Config entries override matching P-codes;
    /// DefaultMap fills gaps for any P-codes not present in the config.
    ///
    /// This merge strategy prevents property drift: when new P-codes are added
    /// to the DefaultMap in code updates, they automatically appear without
    /// requiring manual config edits.
    /// </summary>
    public static IReadOnlyDictionary<string, WikidataProperty> BuildMapFromUniverse(
        UniverseConfiguration universe)
    {
        ArgumentNullException.ThrowIfNull(universe);

        // Start with a copy of DefaultMap — all compiled defaults are present.
        var map = new Dictionary<string, WikidataProperty>(DefaultMap, StringComparer.OrdinalIgnoreCase);

        // Overlay config entries on top — config wins for user-configurable fields.
        // For properties that already exist in DefaultMap, preserve the structural
        // flags (IsMonolingualText, IsEntityValued, IsMultiValued, IsBridge) from
        // DefaultMap. These flags describe how to build the SPARQL query and are
        // not meant to be user-configurable. Config files generated before these
        // flags were introduced will have them missing (defaulting to false), which
        // would silently break language filtering and multi-value handling.
        foreach (var (pCode, config) in universe.PropertyMap)
        {
            if (map.TryGetValue(pCode, out var existing))
            {
                // Known property: overlay user-configurable fields only.
                map[pCode] = existing with
                {
                    ClaimKey       = string.IsNullOrWhiteSpace(config.ClaimKey)    ? existing.ClaimKey    : config.ClaimKey,
                    Category       = string.IsNullOrWhiteSpace(config.Category)    ? existing.Category    : config.Category,
                    EntityScope    = string.IsNullOrWhiteSpace(config.EntityScope) ? existing.EntityScope : config.EntityScope,
                    Confidence     = config.Confidence,
                    Enabled        = config.Enabled,
                    ValueTransform = config.ValueTransform ?? existing.ValueTransform,
                    // Structural flags preserved from DefaultMap (not overridable via config).
                };
            }
            else
            {
                // Brand-new property not in DefaultMap — use all config values.
                map[pCode] = new WikidataProperty
                {
                    PCode             = pCode,
                    ClaimKey          = config.ClaimKey,
                    Category          = config.Category,
                    EntityScope       = config.EntityScope,
                    Confidence        = config.Confidence,
                    IsBridge          = config.IsBridge,
                    Enabled           = config.Enabled,
                    IsEntityValued    = config.IsEntityValued,
                    ValueTransform    = config.ValueTransform,
                    IsMultiValued     = config.IsMultiValued,
                    IsMonolingualText = config.IsMonolingualText,
                };
            }
        }

        return map;
    }

    // ── SPARQL Query Builders ────────────────────────────────────────────────

    /// <summary>
    /// Build a SPARQL SELECT query for all enabled Work-scoped properties of a given QID.
    /// P-codes listed in <paramref name="scopeExclusions"/> are skipped.
    /// Default exclusion: <c>P18</c> (Image) — Person-only due to copyright constraints.
    /// </summary>
    /// <param name="language">
    /// BCP-47 language code (e.g. <c>"en"</c>) used to filter monolingual text properties
    /// like P1476 (title). Falls back to any language if the preferred is unavailable.
    /// Defaults to <c>"en"</c> if <c>null</c> or empty.
    /// </param>
    public static string BuildWorkSparqlQuery(
        string qid,
        IReadOnlyDictionary<string, WikidataProperty>? effectiveMap = null,
        IReadOnlyCollection<string>? scopeExclusions = null,
        string? language = null)
    {
        var exclusions = scopeExclusions ?? (IReadOnlyCollection<string>)["P18"];

        var props = GetByScope("Work", effectiveMap)
            .Where(p => !exclusions.Contains(p.PCode))
            .ToList();

        return BuildSparqlQuery(qid, props, language);
    }

    // ── Core Properties for Pass 1 (Quick Match) ──────────────────────────

    /// <summary>
    /// The P-codes that constitute core Work identity — fetched during Pass 1
    /// (Quick Match) for fast Dashboard appearance. All other properties are
    /// deferred to Pass 2 (Universe Lookup).
    /// </summary>
    private static readonly HashSet<string> CorePropertyPCodes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "P1476", // title (monolingual text)
            "P50",   // author
            "P577",  // year
            "P136",  // genre
            "P179",  // series
            "P1545", // series_position
            "P31",   // instance_of (edition detection)
            "P629",  // edition_or_translation_of (edition → work resolution)
        };

    /// <summary>
    /// Build a SPARQL SELECT for core Work properties only (Pass 1 — Quick Match).
    ///
    /// <para>
    /// Fetches: title, author, year, genre, series, series_position, instance_of,
    /// edition_or_translation_of, plus <b>all bridge identifiers</b> (ISBN, ASIN,
    /// TMDB ID, etc.). Bridge IDs are essential for Stage 3 retail lookups.
    /// </para>
    ///
    /// <para>
    /// Skips: narrative relationships, fictional entities, cast members, awards,
    /// physical/technical details, and all non-bridge properties. These are fetched
    /// in Pass 2 (Universe Lookup).
    /// </para>
    ///
    /// <para>
    /// P18 (Image) is excluded — Person-only due to copyright constraints.
    /// </para>
    /// </summary>
    /// <param name="qid">The Wikidata QID (e.g. <c>"Q190159"</c>).</param>
    /// <param name="effectiveMap">Property map to use. Falls back to <see cref="DefaultMap"/>.</param>
    /// <param name="scopeExclusions">P-codes to exclude. Defaults to <c>["P18"]</c>.</param>
    /// <param name="language">BCP-47 language code. Defaults to <c>"en"</c>.</param>
    public static string BuildCoreWorkSparqlQuery(
        string qid,
        IReadOnlyDictionary<string, WikidataProperty>? effectiveMap = null,
        IReadOnlyCollection<string>? scopeExclusions = null,
        string? language = null)
    {
        var exclusions = scopeExclusions ?? (IReadOnlyCollection<string>)["P18"];

        var props = GetByScope("Work", effectiveMap)
            .Where(p => !exclusions.Contains(p.PCode))
            .Where(p => CorePropertyPCodes.Contains(p.PCode) || p.IsBridge)
            .ToList();

        return BuildSparqlQuery(qid, props, language);
    }

    /// <summary>
    /// Build a SPARQL SELECT query for all enabled Person-scoped properties of a given QID.
    /// Includes P18 (Image) — headshots of public figures are not copyrighted.
    /// </summary>
    /// <param name="language">
    /// BCP-47 language code for filtering monolingual text properties.
    /// Defaults to <c>"en"</c> if <c>null</c> or empty.
    /// </param>
    public static string BuildPersonSparqlQuery(
        string qid,
        IReadOnlyDictionary<string, WikidataProperty>? effectiveMap = null,
        string? language = null)
    {
        var props = GetByScope("Person", effectiveMap);
        return BuildSparqlQuery(qid, props, language);
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

    /// <summary>
    /// Build a SPARQL query that fetches properties for multiple entities in a single
    /// request using the VALUES clause. Maximum batch size controlled by caller.
    /// </summary>
    /// <param name="qids">The Wikidata QIDs to query (e.g. <c>["Q937618", "Q937620"]</c>).</param>
    /// <param name="effectiveMap">Property map to use. Falls back to <see cref="DefaultMap"/>.</param>
    /// <param name="scope">Entity scope filter (e.g. "Character", "Location"). Defaults to "Character".</param>
    /// <param name="scopeExclusions">P-codes to exclude. Defaults to <c>["P18"]</c>.</param>
    /// <param name="language">BCP-47 language code. Defaults to <c>"en"</c>.</param>
    public static string BuildBatchEntityQuery(
        IReadOnlyList<string> qids,
        IReadOnlyDictionary<string, WikidataProperty>? effectiveMap = null,
        string scope = "Character",
        IReadOnlyCollection<string>? scopeExclusions = null,
        string? language = null)
    {
        ArgumentNullException.ThrowIfNull(qids);
        if (qids.Count == 0)
            return string.Empty;

        var exclusions = scopeExclusions ?? (IReadOnlyCollection<string>)["P18"];
        var lang = string.IsNullOrWhiteSpace(language) ? "en" : language;

        var props = GetByScope(scope, effectiveMap)
            .Where(p => !exclusions.Contains(p.PCode))
            .ToList();

        if (props.Count == 0)
            return string.Empty;

        var sb = new StringBuilder(2048);

        // SELECT clause
        sb.Append("SELECT ?item ?itemLabel");
        foreach (var prop in props)
        {
            var varName = prop.ClaimKey;
            sb.Append($" ?{varName}");
            if (prop.IsEntityValued)
                sb.Append($" ?{varName}Label");
        }
        sb.AppendLine(" WHERE {");

        // VALUES clause for batch
        sb.Append("  VALUES ?item {");
        foreach (var qid in qids)
            sb.Append($" wd:{qid}");
        sb.AppendLine(" }");

        // OPTIONAL property clauses
        foreach (var prop in props)
        {
            var varName = prop.ClaimKey;
            if (prop.IsMultiValued)
            {
                sb.AppendLine($"  OPTIONAL {{ ?item wdt:{prop.PCode} ?{varName}_single . }}");
            }
            else if (prop.IsMonolingualText)
            {
                sb.AppendLine($"  OPTIONAL {{ ?item wdt:{prop.PCode} ?{varName} . FILTER(LANG(?{varName}) = \"{lang}\" || LANG(?{varName}) = \"\") }}");
            }
            else
            {
                sb.AppendLine($"  OPTIONAL {{ ?item wdt:{prop.PCode} ?{varName} . }}");
            }
        }

        // Label service
        sb.AppendLine($"  SERVICE wikibase:label {{ bd:serviceParam wikibase:language \"{lang},en\". }}");
        sb.AppendLine("}");

        // GROUP BY for multi-valued properties
        var multiValuedProps = props.Where(p => p.IsMultiValued).ToList();
        if (multiValuedProps.Count > 0)
        {
            sb.Append("GROUP BY ?item ?itemLabel");
            foreach (var prop in props.Where(p => !p.IsMultiValued))
            {
                var varName = prop.ClaimKey;
                sb.Append($" ?{varName}");
                if (prop.IsEntityValued)
                    sb.Append($" ?{varName}Label");
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }

    // ── Edition vs Work Resolution ────────────────────────────────────────

    /// <summary>
    /// Build a lightweight SPARQL query to determine whether a QID is an
    /// edition/translation (P31=Q3331189) and, if so, follow P629 to find
    /// the parent work item.
    ///
    /// <para>
    /// Bridge lookups (ISBN) can return a Wikidata <b>edition</b> item instead
    /// of the <b>work</b> item. Edition items carry physical details (page count)
    /// but lack relational metadata (series, franchise, characters). This query
    /// detects that case so the adapter can switch to the parent work QID for
    /// deep hydration.
    /// </para>
    /// </summary>
    public static string BuildEditionCheckQuery(string qid)
    {
        return $$"""
            SELECT ?instanceOf ?parentWork WHERE {
              OPTIONAL { wd:{{qid}} wdt:P31 ?instanceOf . }
              OPTIONAL { wd:{{qid}} wdt:P629 ?parentWork . }
            } LIMIT 10
            """;
    }

    // ── Author Audit with Qualifiers ──────────────────────────────────────

    /// <summary>
    /// Build a SPARQL query using qualified statement syntax (<c>p:/ps:/pq:</c>)
    /// to extract P50 (author) statements with P1545 ordinal qualifiers and P31
    /// instance-of classification on each author entity.
    ///
    /// <para>
    /// The standard <c>wdt:</c> accessor used by <see cref="BuildWorkSparqlQuery"/>
    /// discards qualifiers. This dedicated query captures author ordering (P1545)
    /// and entity type (human Q5, pseudonym Q61002, collective pseudonym Q127843)
    /// for accurate author display and pseudonym detection.
    /// </para>
    /// </summary>
    /// <param name="qid">The Work QID to audit authors for.</param>
    /// <param name="language">BCP-47 language code for labels. Defaults to <c>"en"</c>.</param>
    public static string BuildAuthorAuditQuery(string qid, string? language = null)
    {
        var lang = string.IsNullOrWhiteSpace(language) ? "en" : language.ToLowerInvariant();
        return $$"""
            SELECT ?author ?authorLabel ?ordinal ?instanceOf WHERE {
              wd:{{qid}} p:P50 ?stmt .
              ?stmt ps:P50 ?author .
              OPTIONAL { ?stmt pq:P1545 ?ordinal . }
              OPTIONAL { ?author wdt:P31 ?instanceOf . }
              ?author rdfs:label ?authorLabel .
              FILTER(LANG(?authorLabel) = "{{lang}}")
            }
            """;
    }

    /// <summary>
    /// Build a SPARQL query to discover the constituent real-person members
    /// of a collective pseudonym entity (e.g. "James S. A. Corey" → Daniel Abraham + Ty Franck).
    ///
    /// <para>
    /// Follows P527 (has parts) and filters to P31=Q5 (human) to find the
    /// real people behind a collective pen name (P31=Q127843).
    /// </para>
    /// </summary>
    /// <param name="qid">The collective pseudonym QID.</param>
    /// <param name="language">BCP-47 language code for labels. Defaults to <c>"en"</c>.</param>
    public static string BuildCollectiveMembersQuery(string qid, string? language = null)
    {
        var lang = string.IsNullOrWhiteSpace(language) ? "en" : language.ToLowerInvariant();
        return $$"""
            SELECT ?member ?memberLabel WHERE {
              wd:{{qid}} wdt:P527 ?member .
              ?member wdt:P31 wd:Q5 .
              ?member rdfs:label ?memberLabel .
              FILTER(LANG(?memberLabel) = "{{lang}}")
            }
            """;
    }

    /// <summary>Builds a SPARQL query to fetch cast members with their character roles (P161 + P453 qualifier).</summary>
    /// <param name="qid">The Work QID to fetch cast for.</param>
    /// <param name="language">BCP-47 language code for labels. Defaults to <c>"en"</c>.</param>
    public static string BuildCastRoleQuery(string qid, string? language = null)
    {
        var lang = string.IsNullOrWhiteSpace(language) ? "en" : language.ToLowerInvariant();
        return $$"""
            SELECT ?actor ?actorLabel ?character ?characterLabel WHERE {
              wd:{{qid}} p:P161 ?stmt .
              ?stmt ps:P161 ?actor .
              OPTIONAL { ?stmt pq:P453 ?character .
                         ?character rdfs:label ?characterLabel .
                         FILTER(LANG(?characterLabel) = "{{lang}}") }
              ?actor rdfs:label ?actorLabel .
              FILTER(LANG(?actorLabel) = "{{lang}}")
            }
            LIMIT 50
            """;
    }

    /// <summary>Builds a SPARQL query to fetch awards received (P166) with preferred rank only (winners, not nominees).</summary>
    public static string BuildAwardsQuery(string qid, string language = "en")
    {
        var lang = string.IsNullOrWhiteSpace(language) ? "en" : language.ToLowerInvariant();
        return $$"""
            SELECT ?award ?awardLabel ?pointInTime WHERE {
              wd:{{qid}} p:P166 ?stmt .
              ?stmt ps:P166 ?award .
              ?stmt wikibase:rank wikibase:PreferredRank .
              OPTIONAL { ?stmt pq:P585 ?pointInTime . }
              ?award rdfs:label ?awardLabel .
              FILTER(LANG(?awardLabel) = "{{lang}}")
            }
            LIMIT 50
            """;
    }

    /// <summary>
    /// Build a SPARQL SELECT query for all enabled Character-scoped properties of a given QID.
    /// Used to enrich fictional characters with gender, species, relationships, etc.
    /// P18 is excluded — character images are resolved via performer Person QIDs.
    /// </summary>
    public static string BuildCharacterSparqlQuery(
        string qid,
        IReadOnlyDictionary<string, WikidataProperty>? effectiveMap = null,
        IReadOnlyCollection<string>? scopeExclusions = null,
        string? language = null)
    {
        var exclusions = scopeExclusions ?? (IReadOnlyCollection<string>)["P18"];

        var props = GetByScope("Character", effectiveMap)
            .Where(p => !exclusions.Contains(p.PCode))
            .ToList();

        return BuildSparqlQuery(qid, props, language);
    }

    /// <summary>
    /// Build a SPARQL SELECT query for all enabled Location-scoped properties of a given QID.
    /// Used to enrich fictional locations with hierarchy, coordinates, etc.
    /// P18 is excluded — location images are not stored.
    /// </summary>
    public static string BuildLocationSparqlQuery(
        string qid,
        IReadOnlyDictionary<string, WikidataProperty>? effectiveMap = null,
        IReadOnlyCollection<string>? scopeExclusions = null,
        string? language = null)
    {
        var exclusions = scopeExclusions ?? (IReadOnlyCollection<string>)["P18"];

        var props = GetByScope("Location", effectiveMap)
            .Where(p => !exclusions.Contains(p.PCode))
            .ToList();

        return BuildSparqlQuery(qid, props, language);
    }

    /// <summary>
    /// Build a SPARQL SELECT query for all enabled Organization-scoped properties of a given QID.
    /// Used to enrich fictional organizations with hierarchy, leadership, etc.
    /// P18 is excluded — organization images are not stored.
    /// </summary>
    public static string BuildOrganizationSparqlQuery(
        string qid,
        IReadOnlyDictionary<string, WikidataProperty>? effectiveMap = null,
        IReadOnlyCollection<string>? scopeExclusions = null,
        string? language = null)
    {
        var exclusions = scopeExclusions ?? (IReadOnlyCollection<string>)["P18"];

        var props = GetByScope("Organization", effectiveMap)
            .Where(p => !exclusions.Contains(p.PCode))
            .ToList();

        return BuildSparqlQuery(qid, props, language);
    }

    /// <summary>
    /// Builds a SPARQL query that searches for works by label, filtering by instance_of
    /// classes and optionally cross-validating with author/director/year.
    /// Used by Tier 2 (Structured SPARQL Search) in the WikidataAdapter.
    /// </summary>
    /// <param name="title">The title to search for (matched against rdfs:label).</param>
    /// <param name="mediaType">The media type string (e.g. "Books", "Movies") — used to look up instance_of classes.</param>
    /// <param name="instanceOfClasses">Map of media type → list of Wikidata Q-IDs for instance_of filtering.</param>
    /// <param name="author">Optional author/director/performer name for cross-validation.</param>
    /// <param name="year">Optional publication year for cross-validation.</param>
    /// <param name="language">BCP-47 language code. Defaults to "en".</param>
    /// <param name="limit">Maximum results to return. Defaults to 10.</param>
    /// <returns>SPARQL query string, or empty string if mediaType has no instance_of classes configured.</returns>
    public static string BuildStructuredSearchQuery(
        string title,
        string mediaType,
        IReadOnlyDictionary<string, List<string>> instanceOfClasses,
        string? author = null,
        string? year = null,
        string? language = null,
        int limit = 10)
    {
        var lang = string.IsNullOrWhiteSpace(language) ? "en" : language.ToLowerInvariant();

        // Look up instance_of classes for this media type.
        if (!instanceOfClasses.TryGetValue(mediaType, out var classes) || classes.Count == 0)
            return string.Empty;

        var escapedTitle = EscapeSparqlString(title);
        var sb = new StringBuilder(1024);

        sb.AppendLine("SELECT ?item ?itemLabel ?itemDescription ?authorLabel ?year WHERE {");

        // instance_of filtering using VALUES + P31/P279* (subclass traversal).
        sb.Append("  VALUES ?class { ");
        foreach (var cls in classes)
            sb.Append("wd:").Append(cls).Append(' ');
        sb.AppendLine("}");
        sb.AppendLine("  ?item wdt:P31/wdt:P279* ?class .");

        // Label match — exact case-insensitive match on rdfs:label.
        sb.Append("  ?item rdfs:label ?label . FILTER(LANG(?label) = \"").Append(lang).AppendLine("\")");
        sb.Append("  FILTER(LCASE(STR(?label)) = LCASE(\"").Append(escapedTitle).AppendLine("\"))");

        // Optional author/director/performer cross-validation.
        if (!string.IsNullOrWhiteSpace(author))
        {
            sb.AppendLine("  OPTIONAL {");
            sb.AppendLine("    { ?item wdt:P50 ?author } UNION { ?item wdt:P57 ?author } UNION { ?item wdt:P175 ?author }");
            sb.Append("    ?author rdfs:label ?authorLabel . FILTER(LANG(?authorLabel) = \"").Append(lang).AppendLine("\")");
            sb.AppendLine("  }");
        }

        // Optional year cross-validation.
        sb.AppendLine("  OPTIONAL { ?item wdt:P577 ?pubDate }");
        sb.AppendLine("  BIND(STR(YEAR(?pubDate)) AS ?year)");

        // Fetch labels and descriptions via the Wikidata label service.
        sb.Append("  SERVICE wikibase:label { bd:serviceParam wikibase:language \"").Append(lang).AppendLine(",en\" . }");

        sb.AppendLine("}");
        sb.Append("LIMIT ").Append(limit);

        return sb.ToString();
    }

    /// <summary>
    /// Builds a lightweight ASK query to check whether a QID's P31 (instance_of)
    /// matches any expected class for a given media type.
    /// Used by Tier 3 post-filtering in the WikidataAdapter.
    /// </summary>
    /// <param name="qid">The Wikidata Q-identifier to check (e.g. "Q190192").</param>
    /// <param name="mediaType">The media type string (e.g. "Books", "Movies").</param>
    /// <param name="instanceOfClasses">Map of media type → list of Wikidata Q-IDs for instance_of filtering.</param>
    /// <returns>SPARQL ASK query string, or empty string if mediaType has no instance_of classes configured.</returns>
    public static string BuildInstanceOfCheckQuery(
        string qid,
        string mediaType,
        IReadOnlyDictionary<string, List<string>> instanceOfClasses)
    {
        if (!instanceOfClasses.TryGetValue(mediaType, out var classes) || classes.Count == 0)
            return string.Empty;

        var sb = new StringBuilder(256);
        sb.AppendLine("ASK {");
        sb.Append("  VALUES ?class { ");
        foreach (var cls in classes)
            sb.Append("wd:").Append(cls).Append(' ');
        sb.AppendLine("}");
        sb.Append("  wd:").Append(qid).AppendLine(" wdt:P31/wdt:P279* ?class .");
        sb.AppendLine("}");

        return sb.ToString();
    }

    /// <summary>Escapes a string for use in a SPARQL literal (doubles backslashes and quotes).</summary>
    private static string EscapeSparqlString(string value)
        => value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    // ── Private Helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// The separator used in SPARQL GROUP_CONCAT for multi-valued properties.
    /// The adapter splits on this to produce individual claims.
    /// </summary>
    public const string MultiValueSeparator = "|||";

    /// <summary>
    /// Builds the Wikidata SPARQL query for a given QID and set of properties.
    /// Uses OPTIONAL clauses so missing properties don't exclude the entity.
    /// For entity-valued properties, fetches rdfs:label in the preferred language.
    /// For monolingual text properties, filters to the preferred language.
    /// For multi-valued properties, uses GROUP_CONCAT to collect all values.
    /// </summary>
    /// <param name="language">
    /// BCP-47 language code for filtering. Defaults to <c>"en"</c> if null or empty.
    /// </param>
    private static string BuildSparqlQuery(string qid, IReadOnlyList<WikidataProperty> props,
        string? language = null)
    {
        var lang = string.IsNullOrWhiteSpace(language) ? "en" : language.ToLowerInvariant();
        if (props.Count == 0)
            return string.Empty;

        var sb = new StringBuilder(2048);
        var hasMultiValued = props.Any(p => p.IsMultiValued);

        // SELECT clause — one variable per property.
        // Multi-valued properties use GROUP_CONCAT in the SELECT.
        sb.Append("SELECT");
        foreach (var p in props)
        {
            var varName = p.PCode.ToLowerInvariant(); // e.g. ?p179

            if (p.IsMultiValued)
            {
                // GROUP_CONCAT for multi-valued: collects all values with ||| separator.
                if (p.IsEntityValued)
                {
                    // Concatenate the labels (human-readable), not the raw QIDs.
                    sb.Append(" (GROUP_CONCAT(DISTINCT ?").Append(varName).Append("Label; separator=\"")
                      .Append(MultiValueSeparator).Append("\") AS ?").Append(varName).Append("Labels)");
                    // Also concatenate raw entity URIs bounded together with their English labels for accurate QID extraction (Hub relationships).
                    sb.Append(" (GROUP_CONCAT(DISTINCT CONCAT(STR(?").Append(varName).Append("), \"::\", COALESCE(?").Append(varName).Append("Label, \"\")); separator=\"")
                      .Append(MultiValueSeparator).Append("\") AS ?").Append(varName).Append("Uris)");
                }
                else
                {
                    sb.Append(" (GROUP_CONCAT(DISTINCT ?").Append(varName).Append("; separator=\"")
                      .Append(MultiValueSeparator).Append("\") AS ?").Append(varName).Append("All)");
                }
            }
            else
            {
                sb.Append(" ?").Append(varName);
                if (p.IsEntityValued)
                    sb.Append(" ?").Append(varName).Append("Label");
            }
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

            if (p.IsEntityValued)
            {
                sb.Append("?").Append(varName)
                  .Append(" rdfs:label ?").Append(varName).Append("Label . ")
                  .Append("FILTER(LANG(?").Append(varName).Append("Label) = \"").Append(lang).Append("\") ");
            }
            else if (p.IsMonolingualText)
            {
                // Monolingual text properties (e.g. P1476 title) have language-tagged
                // literals. Filter to the user's preferred language to avoid getting
                // Czech/Bulgarian/etc. translations.
                sb.Append("FILTER(LANG(?").Append(varName).Append(") = \"").Append(lang).Append("\") ");
            }

            sb.AppendLine("}");
        }

        sb.AppendLine("}");

        // GROUP BY is needed when GROUP_CONCAT is used — group on all non-aggregated variables.
        if (hasMultiValued)
        {
            var nonAggregated = props.Where(p => !p.IsMultiValued).ToList();
            if (nonAggregated.Count > 0)
            {
                sb.Append("GROUP BY");
                foreach (var p in nonAggregated)
                {
                    var varName = p.PCode.ToLowerInvariant();
                    sb.Append(" ?").Append(varName);
                    if (p.IsEntityValued)
                        sb.Append(" ?").Append(varName).Append("Label");
                }
                sb.AppendLine();
            }
        }

        sb.AppendLine("LIMIT 1");

        return sb.ToString();
    }

    // ── Default Map Builder ──────────────────────────────────────────────────

    private static Dictionary<string, WikidataProperty> BuildDefaultMap()
    {
        var map = new Dictionary<string, WikidataProperty>(116, StringComparer.OrdinalIgnoreCase);

        void Add(string pCode, string claimKey, string category,
                 string scope = "Work", double confidence = 0.9, bool bridge = false,
                 bool entityValued = false, string? transform = null, bool multiValued = false,
                 bool monolingualText = false)
        {
            map[pCode] = new WikidataProperty
            {
                PCode            = pCode,
                ClaimKey         = claimKey,
                Category         = category,
                EntityScope      = scope,
                Confidence       = confidence,
                IsBridge         = bridge,
                IsEntityValued   = entityValued,
                ValueTransform   = transform,
                IsMultiValued    = multiValued,
                IsMonolingualText = monolingualText,
            };
        }

        // ── Stage 1: Work Identity ────────────────────────────────────────
        Add("P31",   "instance_of",     "Stage 1: Work Identity", scope: "Work,Character,Location,Organization", entityValued: true, multiValued: true);
        Add("P1476", "title",           "Stage 1: Work Identity", monolingualText: true);
        Add("P577",  "year",            "Stage 1: Work Identity",                       transform: "year_from_iso");
        Add("P407",  "language",        "Stage 1: Work Identity", confidence: 0.85,     entityValued: true);
        Add("P495",  "country_of_origin", "Stage 1: Work Identity", confidence: 0.85,   entityValued: true);
        Add("P123",  "publisher",       "Stage 1: Work Identity", confidence: 0.85,     entityValued: true);
        Add("P136",  "genre",           "Stage 1: Work Identity", confidence: 0.8,      entityValued: true, multiValued: true);
        Add("P629",  "edition_or_translation_of", "Stage 1: Work Identity", confidence: 0.9, entityValued: true);
        Add("P825",  "adaptation_of",   "Stage 1: Work Identity", confidence: 0.8,      entityValued: true, multiValued: true);

        // ── Stage 1: Series & Franchise ───────────────────────────────────
        Add("P179",  "series",          "Stage 1: Series & Franchise",                       entityValued: true, multiValued: true);
        Add("P1545", "series_position", "Stage 1: Series & Franchise",                       transform: "numeric_portion");
        Add("P8345", "franchise",       "Stage 1: Series & Franchise",                       entityValued: true, multiValued: true);
        Add("P155",  "preceded_by",     "Stage 1: Series & Franchise", confidence: 0.8,      entityValued: true, multiValued: true);
        Add("P156",  "followed_by",     "Stage 1: Series & Franchise", confidence: 0.8,      entityValued: true, multiValued: true);

        // ── Stage 1: Creative Credits (Work → Person links) ──────────────
        Add("P50",   "author",              "Stage 1: Creative Credits",                          entityValued: true, multiValued: true);
        Add("P110",  "illustrator",         "Stage 1: Creative Credits",                          entityValued: true, multiValued: true);
        Add("P57",   "director",            "Stage 1: Creative Credits",                          entityValued: true, multiValued: true);
        Add("P161",  "cast_member",         "Stage 1: Creative Credits",                          entityValued: true, multiValued: true);
        Add("P987",  "narrator",            "Stage 1: Creative Credits",                          entityValued: true, multiValued: true);
        Add("P725",  "voice_actor",         "Stage 1: Creative Credits",                          entityValued: true, multiValued: true);
        Add("P58",   "screenwriter",        "Stage 1: Creative Credits",                          entityValued: true, multiValued: true);
        Add("P86",   "composer",            "Stage 1: Creative Credits",                          entityValued: true, multiValued: true);
        Add("P175",  "performer",           "Stage 1: Creative Credits",                          entityValued: true, multiValued: true);
        Add("P2093", "author_name_string",  "Stage 1: Creative Credits", confidence: 0.7);
        Add("P655",  "translator",          "Stage 1: Creative Credits", confidence: 0.85,        entityValued: true, multiValued: true);
        Add("P1431", "executive_producer",  "Stage 1: Creative Credits", confidence: 0.85,        entityValued: true, multiValued: true);

        // ── Stage 1: Story & Narrative ────────────────────────────────────
        Add("P840",  "narrative_location", "Stage 1: Story & Narrative", confidence: 0.8, entityValued: true, multiValued: true);
        Add("P674",  "characters",         "Stage 1: Story & Narrative", confidence: 0.8, entityValued: true, multiValued: true);
        Add("P921",  "main_subject",       "Stage 1: Story & Narrative", confidence: 0.8, entityValued: true, multiValued: true);
        Add("P1434", "fictional_universe", "Stage 1: Story & Narrative", scope: "Work,Character,Location,Organization", confidence: 0.8, entityValued: true, multiValued: true);
        Add("P144",  "based_on",           "Stage 1: Story & Narrative", confidence: 0.8, entityValued: true, multiValued: true);
        Add("P4584", "first_appearance",   "Stage 1: Story & Narrative", confidence: 0.8, entityValued: true, multiValued: true);

        // ── Stage 1: Bridges — Books ─────────────────────────────────────
        Add("P3861", "apple_books_id", "Stage 1: Bridges — Books", confidence: 1.0, bridge: true);
        Add("P212",  "isbn",           "Stage 1: Bridges — Books", confidence: 1.0, bridge: true);
        Add("P1566", "asin",           "Stage 1: Bridges — Books", confidence: 1.0, bridge: true);
        Add("P2969", "goodreads_id",   "Stage 1: Bridges — Books", confidence: 1.0, bridge: true);
        Add("P244",  "loc_id",         "Stage 1: Bridges — Books", confidence: 1.0, bridge: true);
        Add("P648",  "openlibrary_id", "Stage 1: Bridges — Books", confidence: 1.0, bridge: true);

        // ── Stage 1: Bridges — Movies/TV ─────────────────────────────────
        Add("P4947", "tmdb_id",       "Stage 1: Bridges — Movies/TV", confidence: 1.0, bridge: true);
        Add("P345",  "imdb_id",       "Stage 1: Bridges — Movies/TV", confidence: 1.0, bridge: true);
        Add("P9385", "justwatch_id",  "Stage 1: Bridges — Movies/TV", confidence: 1.0, bridge: true);
        Add("P1712", "metacritic_id", "Stage 1: Bridges — Movies/TV", confidence: 1.0, bridge: true);
        Add("P6127", "letterboxd_id", "Stage 1: Bridges — Movies/TV", confidence: 1.0, bridge: true);
        Add("P2638", "tvcom_id",      "Stage 1: Bridges — Movies/TV", confidence: 1.0, bridge: true);
        Add("P4835", "tvdb_id",       "Stage 1: Bridges — Movies/TV", confidence: 1.0, bridge: true);

        // ── Stage 1: Bridges — Comics/Anime ──────────────────────────────
        Add("P3589",  "gcd_series_id", "Stage 1: Bridges — Comics/Anime", confidence: 1.0, bridge: true);
        Add("P11308", "gcd_issue_id",  "Stage 1: Bridges — Comics/Anime", confidence: 1.0, bridge: true);
        Add("P5905",  "comicvine_id",  "Stage 1: Bridges — Comics/Anime", confidence: 1.0, bridge: true);
        Add("P4084",  "mal_anime_id",  "Stage 1: Bridges — Comics/Anime", confidence: 1.0, bridge: true);
        Add("P4087",  "mal_manga_id",  "Stage 1: Bridges — Comics/Anime", confidence: 1.0, bridge: true);
        Add("P11736", "anilist_id",    "Stage 1: Bridges — Comics/Anime", confidence: 1.0, bridge: true);

        // ── Stage 1: Bridges — Music/Audio ───────────────────────────────
        Add("P434",  "musicbrainz_id",               "Stage 1: Bridges — Music/Audio", confidence: 1.0, bridge: true);
        Add("P1902", "spotify_id",                   "Stage 1: Bridges — Music/Audio", confidence: 1.0, bridge: true);
        Add("P1953", "discogs_id",                   "Stage 1: Bridges — Music/Audio", confidence: 1.0, bridge: true);
        Add("P3398", "audible_id",                   "Stage 1: Bridges — Music/Audio", confidence: 1.0, bridge: true);
        Add("P982",  "musicbrainz_release_group_id", "Stage 1: Bridges — Music/Audio", confidence: 1.0, bridge: true);

        // ── Stage 1: Physical/Technical ──────────────────────────────────
        Add("P1680", "subtitle",       "Stage 1: Physical/Technical", confidence: 0.85);
        Add("P2047", "duration",       "Stage 1: Physical/Technical", confidence: 0.85, transform: "duration_from_quantity");
        Add("P1104", "page_count",     "Stage 1: Physical/Technical", confidence: 0.85, transform: "numeric_portion");
        Add("P1657", "maturity_rating","Stage 1: Physical/Technical", confidence: 0.8,  entityValued: true);
        Add("P433",  "issue_number",   "Stage 1: Physical/Technical", confidence: 0.85);
        Add("P478",  "volume_number",  "Stage 1: Physical/Technical", confidence: 0.85);

        // ── Stage 1: Bridges — Games ──────────────────────────────────────
        Add("P5792", "igdb_id",      "Stage 1: Bridges — Games", confidence: 1.0, bridge: true);
        Add("P9968", "rawg_id",      "Stage 1: Bridges — Games", confidence: 1.0, bridge: true);
        Add("P1735", "steam_appid",  "Stage 1: Bridges — Games", confidence: 1.0, bridge: true);

        // Stage 1: Social Proof (data comes from BuildAwardsQuery, not standard wdt: query)
        Add("P166", "awards_received", "Stage 1: Social Proof", confidence: 0.85, entityValued: true, multiValued: true);

        // ── Person Enrichment (Person-scoped — runs via RecursiveIdentityService) ──
        // P18 (Image): Person-only — copyright constraint.
        // Wikimedia Commons headshots of public figures are not copyrighted.
        // Media cover art comes exclusively from Apple Books, Audnexus, and TMDB.
        Add("P800", "notable_work", "Person Enrichment", scope: "Person", confidence: 0.85, entityValued: true, multiValued: true);
        Add("P18",  "headshot_url", "Person Enrichment", scope: "Person",                  transform: "commons_url");
        Add("P106", "occupation",   "Person Enrichment", scope: "Person,Character", confidence: 0.85, entityValued: true, multiValued: true);
        Add("P19",   "place_of_birth",        "Person Enrichment", scope: "Person", confidence: 0.9);
        Add("P20",   "place_of_death",        "Person Enrichment", scope: "Person", confidence: 0.9);
        Add("P27",   "country_of_citizenship","Person Enrichment", scope: "Person", confidence: 0.9);
        Add("P569",  "date_of_birth",   "Person Enrichment", scope: "Character,Person", confidence: 0.9, transform: "date_with_precision");
        Add("P570",  "date_of_death",   "Person Enrichment", scope: "Character,Person", confidence: 0.9, transform: "date_with_precision");
        Add("P742",  "pseudonym",             "Person Enrichment", scope: "Person", confidence: 0.85, multiValued: true, entityValued: true);
        Add("P1773", "attributed_to",         "Person Enrichment", scope: "Person", confidence: 0.85, multiValued: true, entityValued: true);
        Add("P1813", "short_name",            "Person Enrichment", scope: "Person", confidence: 0.8, monolingualText: true);

        // ── Person: Social Links (Person-scoped) ─────────────────────────
        Add("P2003", "instagram", "Person: Social Links", scope: "Person", confidence: 1.0, bridge: true);
        Add("P2002", "twitter",   "Person: Social Links", scope: "Person", confidence: 1.0, bridge: true);
        Add("P7085", "tiktok",    "Person: Social Links", scope: "Person", confidence: 1.0, bridge: true);
        Add("P4033", "mastodon",  "Person: Social Links", scope: "Person", confidence: 1.0, bridge: true);
        Add("P856",  "website",   "Person: Social Links", scope: "Person", confidence: 1.0, bridge: true);

        // ── Universe: Character ───────────────────────────────────────────
        // Identity properties for fictional characters.
        Add("P1441", "present_in_work", "Universe: Character", scope: "Character", confidence: 0.85, entityValued: true, multiValued: true);
        Add("P170",  "creator",         "Universe: Character", scope: "Character", confidence: 0.85, entityValued: true);
        Add("P21",   "gender",          "Universe: Character", scope: "Character", confidence: 0.9,  entityValued: true);
        Add("P171",  "species",         "Universe: Character", scope: "Character", confidence: 0.85, entityValued: true);
        Add("P103",  "native_language", "Universe: Character", scope: "Character", confidence: 0.8, entityValued: true);
        Add("P1281", "avatar_image",    "Universe: Character", scope: "Character", confidence: 0.8, transform: "commons_url");

        // ── Universe: Character Relationships ─────────────────────────────
        // Entity-valued so they emit _qid claims for graph edges.
        Add("P22",   "father",          "Universe: Character Relationships", scope: "Character", confidence: 0.85, entityValued: true);
        Add("P25",   "mother",          "Universe: Character Relationships", scope: "Character", confidence: 0.85, entityValued: true);
        Add("P26",   "spouse",          "Universe: Character Relationships", scope: "Character", confidence: 0.85, entityValued: true, multiValued: true);
        Add("P3373", "sibling",         "Universe: Character Relationships", scope: "Character", confidence: 0.85, entityValued: true, multiValued: true);
        Add("P40",   "child",           "Universe: Character Relationships", scope: "Character", confidence: 0.85, entityValued: true, multiValued: true);
        Add("P1344", "opponent",        "Universe: Character Relationships", scope: "Character", confidence: 0.8,  entityValued: true, multiValued: true);
        Add("P1066", "student_of",      "Universe: Character Relationships", scope: "Character", confidence: 0.8,  entityValued: true);
        Add("P463",  "member_of",       "Universe: Character Relationships", scope: "Character", confidence: 0.85, entityValued: true, multiValued: true);
        Add("P551",  "residence",       "Universe: Character Relationships", scope: "Character", confidence: 0.8,  entityValued: true, multiValued: true);
        Add("P3342", "significant_person", "Universe: Character Relationships", scope: "Character", confidence: 0.8, entityValued: true, multiValued: true);
        Add("P1416", "affiliation",        "Universe: Character Relationships", scope: "Character", confidence: 0.8, entityValued: true, multiValued: true);

        // ── Universe: Location ────────────────────────────────────────────
        Add("P131",  "located_in",          "Universe: Location", scope: "Location",              confidence: 0.85, entityValued: true);
        Add("P361",  "part_of",             "Universe: Location", scope: "Location,Organization", confidence: 0.85, entityValued: true);
        Add("P625",  "coordinate_location", "Universe: Location", scope: "Location",              confidence: 0.8);

        // ── Universe: Organization ────────────────────────────────────────
        Add("P527",  "has_parts",           "Universe: Organization", scope: "Organization", confidence: 0.85, entityValued: true, multiValued: true);
        Add("P169",  "head_of",             "Universe: Organization", scope: "Organization", confidence: 0.85, entityValued: true);
        Add("P749",  "parent_organization", "Universe: Organization", scope: "Organization", confidence: 0.85, entityValued: true);

        return map;
    }
}

/// <summary>
/// A user-defined override for a single Wikidata property from the legacy manifest.
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
