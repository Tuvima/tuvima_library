using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MediaEngine.Domain.Models;
using MediaEngine.Domain.Services;
using MediaEngine.Domain.Constants;
using MediaEngine.Domain.Entities;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Storage;

/// <summary>
/// Translates collection rule predicates into SQL queries against the works + canonical_values tables.
/// Used for query-resolved collections (Smart, Custom, Discovery).
///
/// Phase 4 — lineage-aware. Canonical values may live on the asset row (Self-scope:
/// title, episode_title, hero, runtime/director for TV) or on the topmost Work row
/// (Parent-scope: author, genre, cover, description, runtime/director for Movies).
/// Because the same field key (e.g. "year", "director") may be Self for one media
/// type and Parent for another, the rule evaluator unions BOTH lookup paths so a
/// single predicate matches works regardless of where the value is stored.
/// </summary>
public sealed class CollectionRuleEvaluator
{
    private readonly IDatabaseConnection _db;

    /// <summary>
    /// Builds a "w.id IN (...)" clause that finds works whose canonical scalar
    /// or array row matches <paramref name="cvPredicate"/> on EITHER the asset row
    /// (Self-scope) or the root parent Work row (Parent-scope, walking
    /// parent_work_id up two levels).
    /// </summary>
    private static string EntityIdsForWork(string workAlias = "w") => $"""
        SELECT {workAlias}.id
        UNION SELECT p.id FROM works p WHERE p.id = {workAlias}.parent_work_id
        UNION SELECT gp.id FROM works p INNER JOIN works gp ON gp.id = p.parent_work_id WHERE p.id = {workAlias}.parent_work_id
        UNION SELECT e.id FROM editions e WHERE e.work_id = {workAlias}.id
        UNION SELECT ma.id FROM editions e INNER JOIN media_assets ma ON ma.edition_id = e.id WHERE e.work_id = {workAlias}.id
        """;

    private static string CvLookup(string cvPredicate, bool negate = false, bool arraysOnly = false)
    {
        var exists = negate ? "NOT EXISTS" : "EXISTS";
        var scalar = arraysOnly
            ? null
            : $"SELECT 1 FROM canonical_values cv WHERE cv.entity_id IN ({EntityIdsForWork()}) AND {cvPredicate}";
        var array = $"SELECT 1 FROM canonical_value_arrays cv WHERE cv.entity_id IN ({EntityIdsForWork()}) AND {cvPredicate}";
        var body = scalar is null ? array : $"{scalar} UNION ALL {array}";
        return $$"""
            {{exists}} (
                {{body}}
            )
            """;
    }

    private static string IsUnknownLookup(string fieldParam) => $$"""
        {{CvLookup($"cv.key = {fieldParam}", negate: true)}}
        AND EXISTS (
            SELECT 1
            FROM entity_capability_states ecs
            WHERE ecs.entity_id IN ({{EntityIdsForWork()}})
              AND ecs.capability_id = '{{CapabilityId.EnrichmentStructuredDiscoveryMetadata}}'
              AND ecs.sub_key = {{fieldParam}}
              AND ecs.status = '{{EntityCapabilityStatus.NoResult}}'
        )
        """;

    /// <summary>
    /// Correlated scalar subquery that resolves a canonical value for the
    /// outer-row work <c>w</c>. Checks asset row first, then walks parent_work_id
    /// up two levels to the topmost Work row. Used in ORDER BY clauses.
    /// </summary>
    private static string CvForWork(string keyParam) => $$"""
        COALESCE(
            (SELECT cv.value FROM editions e_cv
             INNER JOIN media_assets ma_cv ON ma_cv.edition_id = e_cv.id
             INNER JOIN canonical_values cv ON cv.entity_id = ma_cv.id
             WHERE e_cv.work_id = w.id AND cv.key = {{keyParam}} LIMIT 1),
            (SELECT cv.value FROM works w_p
             LEFT JOIN works p_p  ON p_p.id  = w_p.parent_work_id
             LEFT JOIN works gp_p ON gp_p.id = p_p.parent_work_id
             INNER JOIN canonical_values cv ON cv.entity_id = COALESCE(gp_p.id, p_p.id, w_p.id)
             WHERE w_p.id = w.id AND cv.key = {{keyParam}} LIMIT 1),
            (SELECT cv.value FROM editions e_cv
             INNER JOIN media_assets ma_cv ON ma_cv.edition_id = e_cv.id
             INNER JOIN canonical_value_arrays cv ON cv.entity_id = ma_cv.id
             WHERE e_cv.work_id = w.id AND cv.key = {{keyParam}}
             ORDER BY cv.ordinal LIMIT 1),
            (SELECT cv.value FROM works w_p
             LEFT JOIN works p_p  ON p_p.id  = w_p.parent_work_id
             LEFT JOIN works gp_p ON gp_p.id = p_p.parent_work_id
             INNER JOIN canonical_value_arrays cv ON cv.entity_id = COALESCE(gp_p.id, p_p.id, w_p.id)
             WHERE w_p.id = w.id AND cv.key = {{keyParam}}
             ORDER BY cv.ordinal LIMIT 1)
        )
        """;

    public CollectionRuleEvaluator(IDatabaseConnection db) => _db = db;

    /// <summary>
    /// Evaluates the given rule predicates and returns matching work IDs from the works table.
    /// </summary>
    public IReadOnlyList<Guid> Evaluate(
        IReadOnlyList<CollectionRulePredicate> predicates,
        string matchMode = "all",
        string? sortField = null,
        string sortDirection = "desc",
        int limit = 0)
    {
        if (predicates.Count == 0) return [];

        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();

        var conditions = new List<string>();
        int paramIdx = 0;

        foreach (var pred in predicates)
        {
            var (sql, parameters) = TranslatePredicate(pred, ref paramIdx);
            if (sql is not null)
            {
                conditions.Add(sql);
                foreach (var (name, value) in parameters)
                {
                    var p = cmd.CreateParameter();
                    p.ParameterName = name;
                    p.Value = value;
                    cmd.Parameters.Add(p);
                }
            }
        }

        if (conditions.Count == 0) return [];

        var joiner = matchMode == "any" ? " OR " : " AND ";
        var whereClause = string.Join(joiner, conditions.Select(c => $"({c})"));

        var orderBy = ResolveOrderBy(sortField, sortDirection);

        var visibleWorkPredicate = HomeVisibilitySql.VisibleWorkPredicate("w.id", "w.curator_state");
        cmd.CommandText = $"""
            SELECT DISTINCT w.id
            FROM works w
            WHERE {visibleWorkPredicate}
              AND ({whereClause})
            {orderBy}
            {(limit > 0 ? $"LIMIT {limit}" : "")}
            """;

        var results = new List<Guid>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            results.Add(GuidSql.FromDb(reader.GetValue(0)));
        }

        return results;
    }

    /// <summary>Computes a SHA-256 hash of normalized rule predicates for deduplication.</summary>
    public static string ComputeRuleHash(IReadOnlyList<CollectionRulePredicate> predicates)
    {
        // Normalize: sort by field+op, lowercase values
        var normalized = predicates
            .OrderBy(p => p.Field, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.Op, StringComparer.OrdinalIgnoreCase)
            .Select(p => new
            {
                field = p.Field.ToLowerInvariant().Trim(),
                op = p.Op.ToLowerInvariant().Trim(),
                values = IsValueFreeOperator(p.Op)
                    ? []
                    : p.GetEffectiveValues().Select(v => v.ToLowerInvariant().Trim()).OrderBy(v => v).ToArray(),
            });

        var json = JsonSerializer.Serialize(normalized);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static bool IsValueFreeOperator(string? op) =>
        op?.Trim().ToLowerInvariant() is "known" or "is_known" or "has_any_value"
            or "unknown" or "is_unknown" or "has_no_known_value";

    /// <summary>Parses RuleJson string into predicates.</summary>
    public static IReadOnlyList<CollectionRulePredicate> ParseRules(string? ruleJson)
    {
        if (string.IsNullOrWhiteSpace(ruleJson)) return [];

        try
        {
            return JsonSerializer.Deserialize<List<CollectionRulePredicate>>(ruleJson,
                MediaEngineJson.CaseInsensitive)
                ?? [];
        }
        catch (JsonException ex)
        {
            throw new FormatException(
                "Collection rule_json must be a JSON array of rule predicates.",
                ex);
        }

    }

    private (string? sql, List<(string name, object value)> parameters) TranslatePredicate(
        CollectionRulePredicate pred, ref int paramIdx)
    {
        var parameters = new List<(string, object)>();
        var field = pred.Field.ToLowerInvariant().Trim();
        var op = pred.Op.ToLowerInvariant().Trim();
        var effectiveValues = pred.GetEffectiveValues();

        if (op is "known" or "is_known" or "has_any_value")
        {
            var pField = $"@p{paramIdx++}";
            parameters.Add((pField, field));
            return (CvLookup($"cv.key = {pField}"), parameters);
        }

        if (op is "unknown" or "is_unknown" or "has_no_known_value")
        {
            if (!StructuredDiscoveryFieldCatalog.TryGet(field, out var definition)
                || definition.Source != DiscoveryFactSource.StructuredProvider)
                return (null, parameters);

            var pField = $"@p{paramIdx++}";
            parameters.Add((pField, field));
            return (IsUnknownLookup(pField), parameters);
        }

        if (effectiveValues.Length == 0) return (null, parameters);

        // Direct work table fields
        if (field == "media_type")
        {
            var pName = $"@p{paramIdx++}";
            parameters.Add((pName, effectiveValues[0]));
            return (op switch
            {
                "neq" => $"w.media_type != {pName}",
                _ => $"w.media_type = {pName}",
            }, parameters);
        }

        if (field == "wikidata_qid")
        {
            var pName = $"@p{paramIdx++}";
            parameters.Add((pName, effectiveValues[0]));
            return ($"w.wikidata_qid = {pName}", parameters);
        }

        // Temporal: added_within_days — use metadata_claims first claim date
        if (field == "added_within_days")
        {
            var pName = $"@p{paramIdx++}";
            parameters.Add((pName, effectiveValues[0]));
            return ($"w.id IN (SELECT e_t.work_id FROM editions e_t INNER JOIN media_assets ma_t ON ma_t.edition_id = e_t.id INNER JOIN metadata_claims mc ON mc.entity_id = ma_t.id GROUP BY e_t.work_id HAVING MIN(mc.claimed_at) >= datetime('now', '-' || {pName} || ' days'))", parameters);
        }

        // Temporal: decade
        if (field == "decade")
        {
            var decadeStr = effectiveValues[0].Replace("s", "");
            if (int.TryParse(decadeStr, out var decadeStart))
            {
                var pStart = $"@p{paramIdx++}";
                var pEnd = $"@p{paramIdx++}";
                parameters.Add((pStart, decadeStart.ToString()));
                parameters.Add((pEnd, (decadeStart + 9).ToString()));
                return (CvLookup($"cv.key = 'year' AND CAST(cv.value AS INTEGER) BETWEEN {pStart} AND {pEnd}"), parameters);
            }
            return (null, parameters);
        }

        // Wikidata franchise — join collection_relationships
        if (field == "wikidata_franchise")
        {
            var pName = $"@p{paramIdx++}";
            parameters.Add((pName, effectiveValues[0]));
            return ($"w.collection_id IN (SELECT hr.collection_id FROM collection_relationships hr WHERE hr.rel_type IN ('franchise','fictional_universe') AND hr.rel_qid = {pName})", parameters);
        }

        // Person QID — match only presentation-grade canonical credits. The raw
        // relationship graph may contain assistant or supplementary credits.
        if (field == "person_qid")
        {
            var predicates = new List<string>();
            foreach (var value in effectiveValues.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var pName = $"@p{paramIdx++}";
                parameters.Add((pName, value));
                predicates.Add($"EXISTS (SELECT 1 FROM editions e_p INNER JOIN media_assets ma_p ON ma_p.edition_id = e_p.id INNER JOIN primary_person_media_credits credit ON credit.media_asset_id = ma_p.id WHERE e_p.work_id = w.id AND credit.person_qid = {pName} COLLATE NOCASE)");
            }

            var joiner = op is "any_of" or "in" ? " OR " : " AND ";
            return ($"({string.Join(joiner, predicates)})", parameters);
        }

        if (field is "has_source_work" or "is_adaptation" or "source_work_owned" or "has_adaptation" or "adaptation_owned")
        {
            var expected = !effectiveValues[0].Equals("false", StringComparison.OrdinalIgnoreCase)
                && effectiveValues[0] != "0";
            var basedOnExists = CvLookup("cv.key = 'based_on'");
            var sourceOwned = $$"""
                EXISTS (
                    SELECT 1
                    FROM canonical_value_arrays source_link
                    WHERE source_link.entity_id IN ({{EntityIdsForWork()}})
                      AND source_link.key = 'based_on'
                      AND EXISTS (
                          SELECT 1 FROM works source_work
                          WHERE source_work.is_catalog_only = 0
                            AND source_work.ownership = 'Owned'
                            AND source_work.wikidata_qid = source_link.value_qid COLLATE NOCASE
                      )
                )
                """;
            var adaptationOwned = $$"""
                NULLIF(w.wikidata_qid, '') IS NOT NULL AND EXISTS (
                    SELECT 1
                    FROM works adaptation
                    INNER JOIN canonical_value_arrays adaptation_link
                      ON adaptation_link.entity_id IN ({{EntityIdsForWork("adaptation")}})
                     AND adaptation_link.key = 'based_on'
                    WHERE adaptation.is_catalog_only = 0
                      AND adaptation.ownership = 'Owned'
                      AND adaptation_link.value_qid = w.wikidata_qid COLLATE NOCASE
                )
                """;
            var expression = field switch
            {
                "has_source_work" or "is_adaptation" => basedOnExists,
                "source_work_owned" => $"{basedOnExists} AND {sourceOwned}",
                _ => adaptationOwned,
            };
            return (expected ? expression : $"NOT ({expression})", parameters);
        }

        // All other fields: canonical_values lookup via edition → asset chain
        var canonicalField = field switch
        {
            "provider_rating" => "rating",
            "user_rating" => "user_rating",
            _ => field,
        };

        return op switch
        {
            "eq" when canonicalField == "user_rating" && effectiveValues[0] == "unrated" =>
                (CvLookup("cv.key = 'user_rating'", negate: true), parameters),
            "eq" => BuildCanonicalEq(canonicalField, effectiveValues[0], ref paramIdx, parameters),
            "neq" => BuildCanonicalNeq(canonicalField, effectiveValues[0], ref paramIdx, parameters),
            "contains" => BuildCanonicalLike(canonicalField, effectiveValues[0], ref paramIdx, parameters),
            "gt" or "gte" or "lt" or "lte" => BuildCanonicalNumeric(canonicalField, op, effectiveValues[0], ref paramIdx, parameters),
            "between" when effectiveValues.Length >= 2 => BuildCanonicalBetween(canonicalField, effectiveValues[0], effectiveValues[1], ref paramIdx, parameters),
            "in" => BuildCanonicalIn(canonicalField, effectiveValues, ref paramIdx, parameters),
            _ => BuildCanonicalEq(canonicalField, effectiveValues[0], ref paramIdx, parameters),
        };
    }

    private static (string sql, List<(string, object)> parameters) BuildCanonicalEq(
        string field, string value, ref int paramIdx, List<(string, object)> parameters)
    {
        var pField = $"@p{paramIdx++}";
        var pValue = $"@p{paramIdx++}";
        parameters.Add((pField, field));
        parameters.Add((pValue, value));
        var isEntity = StructuredDiscoveryFieldCatalog.IsEntityBacked(field);
        var comparison = isEntity ? $"cv.value_qid = {pValue} COLLATE NOCASE" : $"cv.value = {pValue}";
        return (CvLookup($"cv.key = {pField} AND {comparison}", arraysOnly: isEntity), parameters);
    }

    private static (string sql, List<(string, object)> parameters) BuildCanonicalNeq(
        string field, string value, ref int paramIdx, List<(string, object)> parameters)
    {
        var pField = $"@p{paramIdx++}";
        var pValue = $"@p{paramIdx++}";
        parameters.Add((pField, field));
        parameters.Add((pValue, value));
        var isEntity = StructuredDiscoveryFieldCatalog.IsEntityBacked(field);
        var comparison = isEntity ? $"cv.value_qid = {pValue} COLLATE NOCASE" : $"cv.value = {pValue}";
        return ($"{CvLookup($"cv.key = {pField}")} AND {CvLookup($"cv.key = {pField} AND {comparison}", negate: true, arraysOnly: isEntity)}", parameters);
    }

    private static (string sql, List<(string, object)> parameters) BuildCanonicalLike(
        string field, string value, ref int paramIdx, List<(string, object)> parameters)
    {
        var pField = $"@p{paramIdx++}";
        var pValue = $"@p{paramIdx++}";
        parameters.Add((pField, field));
        parameters.Add((pValue, $"%{value}%"));
        return (CvLookup($"cv.key = {pField} AND cv.value LIKE {pValue}"), parameters);
    }

    private static (string sql, List<(string, object)> parameters) BuildCanonicalNumeric(
        string field, string op, string value, ref int paramIdx, List<(string, object)> parameters)
    {
        var sqlOp = op switch { "gt" => ">", "gte" => ">=", "lt" => "<", "lte" => "<=", _ => "=" };
        var pField = $"@p{paramIdx++}";
        var pValue = $"@p{paramIdx++}";
        parameters.Add((pField, field));
        parameters.Add((pValue, value));
        return (CvLookup($"cv.key = {pField} AND CAST(cv.value AS REAL) {sqlOp} CAST({pValue} AS REAL)"), parameters);
    }

    private static (string sql, List<(string, object)> parameters) BuildCanonicalBetween(
        string field, string low, string high, ref int paramIdx, List<(string, object)> parameters)
    {
        var pField = $"@p{paramIdx++}";
        var pLow = $"@p{paramIdx++}";
        var pHigh = $"@p{paramIdx++}";
        parameters.Add((pField, field));
        parameters.Add((pLow, low));
        parameters.Add((pHigh, high));
        return (CvLookup($"cv.key = {pField} AND CAST(cv.value AS REAL) BETWEEN CAST({pLow} AS REAL) AND CAST({pHigh} AS REAL)"), parameters);
    }

    private static (string sql, List<(string, object)> parameters) BuildCanonicalIn(
        string field, string[] values, ref int paramIdx, List<(string, object)> parameters)
    {
        var pField = $"@p{paramIdx++}";
        parameters.Add((pField, field));
        var valueParams = new List<string>();
        foreach (var v in values)
        {
            var pv = $"@p{paramIdx++}";
            parameters.Add((pv, v));
            valueParams.Add(pv);
        }
        var inList = string.Join(", ", valueParams);
        var isEntity = StructuredDiscoveryFieldCatalog.IsEntityBacked(field);
        var column = isEntity ? "cv.value_qid" : "cv.value";
        return (CvLookup($"cv.key = {pField} AND {column} IN ({inList})", arraysOnly: isEntity), parameters);
    }

    private static string ResolveOrderBy(string? sortField, string sortDirection)
    {
        var dir = sortDirection.Equals("asc", StringComparison.OrdinalIgnoreCase) ? "ASC" : "DESC";
        return sortField?.ToLowerInvariant() switch
        {
            "title" => $"ORDER BY {CvForWork("'title'")} {dir}",
            "year" => $"ORDER BY CAST({CvForWork("'year'")} AS INTEGER) {dir}",
            "newest" or "created_at" => $"ORDER BY (SELECT MIN(mc.claimed_at) FROM editions e_mc INNER JOIN media_assets ma_mc ON ma_mc.edition_id = e_mc.id INNER JOIN metadata_claims mc ON mc.entity_id = ma_mc.id WHERE e_mc.work_id = w.id) {dir}",
            _ => $"ORDER BY (SELECT MIN(mc.claimed_at) FROM editions e_mc INNER JOIN media_assets ma_mc ON ma_mc.edition_id = e_mc.id INNER JOIN metadata_claims mc ON mc.entity_id = ma_mc.id WHERE e_mc.work_id = w.id) {dir}",
        };
    }
}
