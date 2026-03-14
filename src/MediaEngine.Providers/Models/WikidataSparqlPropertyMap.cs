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
                new() { PCode = "P212",  RequestField = "isbn"           },
                new() { PCode = "P3861", RequestField = "apple_books_id" },
                new() { PCode = "P3398", RequestField = "audible_id"     },
                new() { PCode = "P4947", RequestField = "tmdb_id"        },
                new() { PCode = "P345",  RequestField = "imdb_id"        },
                new() { PCode = "P1566", RequestField = "asin"           },
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
        var map = new Dictionary<string, WikidataProperty>(96, StringComparer.OrdinalIgnoreCase);

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

        // ── Core Identity (Work-scoped) ──────────────────────────────────
        Add("P31",   "instance_of",     "Core Identity", scope: "Work,Character,Location,Organization", entityValued: true, multiValued: true);
        Add("P136",  "genre",           "Core Identity", confidence: 0.8,      entityValued: true, multiValued: true);
        Add("P1476", "title",           "Core Identity", monolingualText: true);
        Add("P179",  "series",          "Core Identity",                       entityValued: true, multiValued: true);
        Add("P1545", "series_position", "Core Identity",                       transform: "numeric_portion");
        Add("P8345", "franchise",       "Core Identity",                       entityValued: true, multiValued: true);
        Add("P155",  "preceded_by",     "Core Identity", confidence: 0.8,      entityValued: true, multiValued: true);
        Add("P156",  "followed_by",     "Core Identity", confidence: 0.8,      entityValued: true, multiValued: true);
        Add("P577",  "year",            "Core Identity",                       transform: "year_from_iso");
        Add("P407",  "language",        "Core Identity", confidence: 0.85,     entityValued: true);
        Add("P495",  "country_of_origin", "Core Identity", confidence: 0.85,   entityValued: true);
        Add("P123",  "publisher",       "Core Identity", confidence: 0.85,     entityValued: true);
        Add("P175",  "performer",       "People",                              entityValued: true, multiValued: true);
        Add("P825",  "adaptation_of",   "Core Identity", confidence: 0.8,      entityValued: true, multiValued: true);

        // ── People — Work-scoped (link Work → Person QID) ───────────────
        Add("P50",  "author",       "People",                                  entityValued: true, multiValued: true);
        Add("P110", "illustrator",  "People",                                  entityValued: true, multiValued: true);
        Add("P57",  "director",     "People",                                  entityValued: true, multiValued: true);
        Add("P161", "cast_member",  "People",                                  entityValued: true, multiValued: true);
        Add("P987", "narrator",     "People",                                  entityValued: true, multiValued: true);
        Add("P725", "voice_actor",  "People",                                  entityValued: true, multiValued: true);
        Add("P58",  "screenwriter", "People",                                  entityValued: true, multiValued: true);
        Add("P86",  "composer",     "People",                                  entityValued: true, multiValued: true);

        // ── People — Person-scoped (enrich the Person entity itself) ────
        // P18 (Image): Person-only — copyright constraint.
        // Wikimedia Commons headshots of public figures are not copyrighted.
        // Media cover art comes exclusively from Apple Books, Audnexus, and TMDB.
        Add("P800", "notable_work", "People", scope: "Person", confidence: 0.85, entityValued: true, multiValued: true);
        Add("P18",  "headshot_url", "People", scope: "Person",                  transform: "commons_url");
        Add("P106", "occupation",   "People", scope: "Person,Character", confidence: 0.85, entityValued: true, multiValued: true);

        // ── Lore & Narrative (Work-scoped, some shared with Character/Location/Org) ─
        Add("P840",  "narrative_location", "Lore & Narrative", confidence: 0.8, entityValued: true, multiValued: true);
        Add("P674",  "characters",         "Lore & Narrative", confidence: 0.8, entityValued: true, multiValued: true);
        Add("P921",  "main_subject",       "Lore & Narrative", confidence: 0.8, entityValued: true, multiValued: true);
        Add("P1434", "fictional_universe", "Lore & Narrative", scope: "Work,Character,Location,Organization", confidence: 0.8, entityValued: true, multiValued: true);
        Add("P144",  "based_on",           "Lore & Narrative", confidence: 0.8, entityValued: true, multiValued: true);
        Add("P4584", "first_appearance",   "Lore & Narrative", confidence: 0.8, entityValued: true, multiValued: true);

        // ── Character-scoped ──────────────────────────────────────────────
        // Identity properties for fictional characters.
        Add("P1441", "present_in_work", "Character", scope: "Character", confidence: 0.85, entityValued: true, multiValued: true);
        Add("P170",  "creator",         "Character", scope: "Character", confidence: 0.85, entityValued: true);
        Add("P21",   "gender",          "Character", scope: "Character", confidence: 0.9,  entityValued: true);
        Add("P171",  "species",         "Character", scope: "Character", confidence: 0.85, entityValued: true);
        Add("P569",  "date_of_birth",   "Character", scope: "Character,Person", confidence: 0.9, transform: "year_from_iso");
        Add("P570",  "date_of_death",   "Character", scope: "Character,Person", confidence: 0.9, transform: "year_from_iso");

        // Relationship properties — entity-valued so they emit _qid claims for graph edges.
        Add("P22",   "father",          "Character Relationships", scope: "Character", confidence: 0.85, entityValued: true);
        Add("P25",   "mother",          "Character Relationships", scope: "Character", confidence: 0.85, entityValued: true);
        Add("P26",   "spouse",          "Character Relationships", scope: "Character", confidence: 0.85, entityValued: true, multiValued: true);
        Add("P3373", "sibling",         "Character Relationships", scope: "Character", confidence: 0.85, entityValued: true, multiValued: true);
        Add("P40",   "child",           "Character Relationships", scope: "Character", confidence: 0.85, entityValued: true, multiValued: true);
        Add("P1344", "opponent",        "Character Relationships", scope: "Character", confidence: 0.8,  entityValued: true, multiValued: true);
        Add("P1066", "student_of",      "Character Relationships", scope: "Character", confidence: 0.8,  entityValued: true);
        Add("P463",  "member_of",       "Character Relationships", scope: "Character", confidence: 0.85, entityValued: true, multiValued: true);
        Add("P551",  "residence",       "Character Relationships", scope: "Character", confidence: 0.8,  entityValued: true, multiValued: true);

        // ── Location-scoped ───────────────────────────────────────────────
        Add("P131",  "located_in",          "Location", scope: "Location",              confidence: 0.85, entityValued: true);
        Add("P361",  "part_of",             "Location", scope: "Location,Organization", confidence: 0.85, entityValued: true);
        Add("P625",  "coordinate_location", "Location", scope: "Location",              confidence: 0.8);

        // ── Organization-scoped ───────────────────────────────────────────
        Add("P527",  "has_parts",           "Organization", scope: "Organization", confidence: 0.85, entityValued: true, multiValued: true);
        Add("P169",  "head_of",             "Organization", scope: "Organization", confidence: 0.85, entityValued: true);
        Add("P749",  "parent_organization", "Organization", scope: "Organization", confidence: 0.85, entityValued: true);

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

        // ── Person Biographical (Person-scoped) ─────────────────────────
        Add("P19",   "place_of_birth",        "People (Person-scoped)", scope: "Person", confidence: 0.9);
        Add("P20",   "place_of_death",        "People (Person-scoped)", scope: "Person", confidence: 0.9);
        Add("P27",   "country_of_citizenship","People (Person-scoped)", scope: "Person", confidence: 0.9);
        Add("P742",  "pseudonym",             "People (Person-scoped)", scope: "Person", confidence: 0.85, multiValued: true, entityValued: true);
        Add("P1773", "attributed_to",         "People (Person-scoped)", scope: "Person", confidence: 0.85, multiValued: true, entityValued: true);
        Add("P1813", "short_name",            "People (Person-scoped)", scope: "Person", confidence: 0.8, monolingualText: true);

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
