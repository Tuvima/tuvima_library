using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MediaEngine.Domain.Models;
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
    /// Builds a "w.id IN (...)" clause that finds works whose canonical_values
    /// row matches <paramref name="cvPredicate"/> on EITHER the asset row
    /// (Self-scope) or the root parent Work row (Parent-scope, walking
    /// parent_work_id up two levels).
    /// </summary>
    private static string CvLookup(string cvPredicate, bool negate = false)
    {
        var op = negate ? "NOT IN" : "IN";
        return $$"""
            w.id {{op}} (
                SELECT e_cv.work_id FROM editions e_cv
                INNER JOIN media_assets ma_cv ON ma_cv.edition_id = e_cv.id
                INNER JOIN canonical_values cv ON cv.entity_id = ma_cv.id
                WHERE {{cvPredicate}}
                UNION
                SELECT w2.id FROM works w2
                LEFT JOIN works p2  ON p2.id  = w2.parent_work_id
                LEFT JOIN works gp2 ON gp2.id = p2.parent_work_id
                INNER JOIN canonical_values cv ON cv.entity_id = COALESCE(gp2.id, p2.id, w2.id)
                WHERE {{cvPredicate}}
            )
            """;
    }

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
             WHERE w_p.id = w.id AND cv.key = {{keyParam}} LIMIT 1)
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

        cmd.CommandText = $"""
            SELECT DISTINCT w.id
            FROM works w
            WHERE COALESCE(w.curator_state, '') NOT IN ('rejected', 'provisional')
              AND NOT EXISTS (
                  SELECT 1 FROM review_queue rq
                  INNER JOIN media_assets ma_r ON ma_r.id = rq.entity_id
                  INNER JOIN editions e_r ON e_r.id = ma_r.edition_id
                  WHERE e_r.work_id = w.id
                    AND rq.status = 'Pending'
                    AND rq.trigger != 'WritebackFailed'
              )
              AND ({whereClause})
            {orderBy}
            {(limit > 0 ? $"LIMIT {limit}" : "")}
            """;

        var results = new List<Guid>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            if (Guid.TryParse(reader.GetString(0), out var id))
                results.Add(id);
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
                values = p.GetEffectiveValues().Select(v => v.ToLowerInvariant().Trim()).OrderBy(v => v).ToArray(),
            });

        var json = JsonSerializer.Serialize(normalized);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>Parses RuleJson string into predicates.</summary>
    public static IReadOnlyList<CollectionRulePredicate> ParseRules(string? ruleJson)
    {
        if (string.IsNullOrWhiteSpace(ruleJson)) return [];

        try
        {
            // Try new format: array of predicates
            var predicates = JsonSerializer.Deserialize<List<CollectionRulePredicate>>(ruleJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (predicates is { Count: > 0 }) return predicates;
        }
        catch
        {
            // Ignore — try legacy format below
        }

        // Legacy format: {"genre":"Science Fiction","min":3,"media":"Any"}
        try
        {
            using var doc = JsonDocument.Parse(ruleJson);
            var result = new List<CollectionRulePredicate>();
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Name is "min" or "min_items") continue; // Skip — maps to Collection.MinItems
                if (prop.Name is "media" && prop.Value.GetString() is "Any") continue; // Skip "Any" media filter

                var field = prop.Name switch
                {
                    "media" => "media_type",
                    "recency_days" => "added_within_days",
                    "min_rating" => "provider_rating",
                    "unrated" => "user_rating",
                    "person" => prop.Value.GetString()?.Contains("Director") == true ? "director" : "author",
                    _ => prop.Name,
                };

                var op = prop.Name switch
                {
                    "min_rating" => "gte",
                    "unrated" => "eq",
                    "recency_days" => "lte",
                    _ => "eq",
                };

                var value = prop.Name == "unrated" ? "unrated" : prop.Value.ToString();

                result.Add(new CollectionRulePredicate { Field = field, Op = op, Value = value });
            }
            return result;
        }
        catch
        {
            return [];
        }
    }

    private (string? sql, List<(string name, object value)> parameters) TranslatePredicate(
        CollectionRulePredicate pred, ref int paramIdx)
    {
        var parameters = new List<(string, object)>();
        var effectiveValues = pred.GetEffectiveValues();
        if (effectiveValues.Length == 0) return (null, parameters);

        var field = pred.Field.ToLowerInvariant().Trim();
        var op = pred.Op.ToLowerInvariant().Trim();

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

        // Person QID — join work_person_links
        if (field == "person_qid")
        {
            var pName = $"@p{paramIdx++}";
            parameters.Add((pName, effectiveValues[0]));
            return ($"w.id IN (SELECT e_p.work_id FROM editions e_p INNER JOIN media_assets ma_p ON ma_p.edition_id = e_p.id INNER JOIN work_person_links wpl ON wpl.entity_id = ma_p.id INNER JOIN persons per ON per.id = wpl.person_id WHERE per.wikidata_qid = {pName})", parameters);
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
        return (CvLookup($"cv.key = {pField} AND cv.value = {pValue}"), parameters);
    }

    private static (string sql, List<(string, object)> parameters) BuildCanonicalNeq(
        string field, string value, ref int paramIdx, List<(string, object)> parameters)
    {
        var pField = $"@p{paramIdx++}";
        var pValue = $"@p{paramIdx++}";
        parameters.Add((pField, field));
        parameters.Add((pValue, value));
        return (CvLookup($"cv.key = {pField} AND cv.value = {pValue}", negate: true), parameters);
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
        return (CvLookup($"cv.key = {pField} AND cv.value IN ({inList})"), parameters);
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
