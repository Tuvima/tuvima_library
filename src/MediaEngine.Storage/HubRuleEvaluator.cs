using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MediaEngine.Domain.Models;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Storage;

/// <summary>
/// Translates hub rule predicates into SQL queries against the works + canonical_values tables.
/// Used for query-resolved hubs (Smart, Custom, Discovery).
///
/// canonical_values.entity_id points to media_assets.id (not works.id).
/// All canonical_values lookups must join through: works → editions → media_assets → canonical_values.
/// </summary>
public sealed class HubRuleEvaluator
{
    private readonly IDatabaseConnection _db;

    /// <summary>
    /// Subquery that maps a canonical_values lookup to work IDs via the edition → asset chain.
    /// Usage: $"w.id IN ({CvWorkSubquery} AND cv.key = ... AND cv.value = ...)"
    /// </summary>
    private const string CvWorkSubquery =
        "SELECT e_cv.work_id FROM editions e_cv " +
        "INNER JOIN media_assets ma_cv ON ma_cv.edition_id = e_cv.id " +
        "INNER JOIN canonical_values cv ON cv.entity_id = ma_cv.id WHERE 1=1";

    /// <summary>
    /// Subquery to resolve canonical values for a given work (used in ORDER BY / correlated subqueries).
    /// Returns cv.value for the first matching asset under the given work.
    /// </summary>
    private const string CvForWorkSubquery =
        "SELECT cv.value FROM editions e_cv " +
        "INNER JOIN media_assets ma_cv ON ma_cv.edition_id = e_cv.id " +
        "INNER JOIN canonical_values cv ON cv.entity_id = ma_cv.id " +
        "WHERE e_cv.work_id = w.id";

    public HubRuleEvaluator(IDatabaseConnection db) => _db = db;

    /// <summary>
    /// Evaluates the given rule predicates and returns matching work IDs from the works table.
    /// </summary>
    public IReadOnlyList<Guid> Evaluate(
        IReadOnlyList<HubRulePredicate> predicates,
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
            WHERE COALESCE(w.curator_state, '') NOT IN ('rejected')
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
    public static string ComputeRuleHash(IReadOnlyList<HubRulePredicate> predicates)
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
    public static IReadOnlyList<HubRulePredicate> ParseRules(string? ruleJson)
    {
        if (string.IsNullOrWhiteSpace(ruleJson)) return [];

        try
        {
            // Try new format: array of predicates
            var predicates = JsonSerializer.Deserialize<List<HubRulePredicate>>(ruleJson,
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
            var result = new List<HubRulePredicate>();
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Name is "min" or "min_items") continue; // Skip — maps to Hub.MinItems
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

                result.Add(new HubRulePredicate { Field = field, Op = op, Value = value });
            }
            return result;
        }
        catch
        {
            return [];
        }
    }

    private (string? sql, List<(string name, object value)> parameters) TranslatePredicate(
        HubRulePredicate pred, ref int paramIdx)
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
                return ($"w.id IN ({CvWorkSubquery} AND cv.key = 'year' AND CAST(cv.value AS INTEGER) BETWEEN {pStart} AND {pEnd})", parameters);
            }
            return (null, parameters);
        }

        // Wikidata franchise — join hub_relationships
        if (field == "wikidata_franchise")
        {
            var pName = $"@p{paramIdx++}";
            parameters.Add((pName, effectiveValues[0]));
            return ($"w.hub_id IN (SELECT hr.hub_id FROM hub_relationships hr WHERE hr.rel_type IN ('franchise','fictional_universe') AND hr.rel_qid = {pName})", parameters);
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
                ($"w.id NOT IN ({CvWorkSubquery} AND cv.key = 'user_rating')", parameters),
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
        return ($"w.id IN ({CvWorkSubquery} AND cv.key = {pField} AND cv.value = {pValue})", parameters);
    }

    private static (string sql, List<(string, object)> parameters) BuildCanonicalNeq(
        string field, string value, ref int paramIdx, List<(string, object)> parameters)
    {
        var pField = $"@p{paramIdx++}";
        var pValue = $"@p{paramIdx++}";
        parameters.Add((pField, field));
        parameters.Add((pValue, value));
        return ($"w.id NOT IN ({CvWorkSubquery} AND cv.key = {pField} AND cv.value = {pValue})", parameters);
    }

    private static (string sql, List<(string, object)> parameters) BuildCanonicalLike(
        string field, string value, ref int paramIdx, List<(string, object)> parameters)
    {
        var pField = $"@p{paramIdx++}";
        var pValue = $"@p{paramIdx++}";
        parameters.Add((pField, field));
        parameters.Add((pValue, $"%{value}%"));
        return ($"w.id IN ({CvWorkSubquery} AND cv.key = {pField} AND cv.value LIKE {pValue})", parameters);
    }

    private static (string sql, List<(string, object)> parameters) BuildCanonicalNumeric(
        string field, string op, string value, ref int paramIdx, List<(string, object)> parameters)
    {
        var sqlOp = op switch { "gt" => ">", "gte" => ">=", "lt" => "<", "lte" => "<=", _ => "=" };
        var pField = $"@p{paramIdx++}";
        var pValue = $"@p{paramIdx++}";
        parameters.Add((pField, field));
        parameters.Add((pValue, value));
        return ($"w.id IN ({CvWorkSubquery} AND cv.key = {pField} AND CAST(cv.value AS REAL) {sqlOp} CAST({pValue} AS REAL))", parameters);
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
        return ($"w.id IN ({CvWorkSubquery} AND cv.key = {pField} AND CAST(cv.value AS REAL) BETWEEN CAST({pLow} AS REAL) AND CAST({pHigh} AS REAL))", parameters);
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
        return ($"w.id IN ({CvWorkSubquery} AND cv.key = {pField} AND cv.value IN ({inList}))", parameters);
    }

    private static string ResolveOrderBy(string? sortField, string sortDirection)
    {
        var dir = sortDirection.Equals("asc", StringComparison.OrdinalIgnoreCase) ? "ASC" : "DESC";
        return sortField?.ToLowerInvariant() switch
        {
            "title" => $"ORDER BY ({CvForWorkSubquery} AND cv.key = 'title' LIMIT 1) {dir}",
            "year" => $"ORDER BY (SELECT CAST(cv.value AS INTEGER) FROM editions e_cv INNER JOIN media_assets ma_cv ON ma_cv.edition_id = e_cv.id INNER JOIN canonical_values cv ON cv.entity_id = ma_cv.id WHERE e_cv.work_id = w.id AND cv.key = 'year' LIMIT 1) {dir}",
            "newest" or "created_at" => $"ORDER BY (SELECT MIN(mc.claimed_at) FROM editions e_mc INNER JOIN media_assets ma_mc ON ma_mc.edition_id = e_mc.id INNER JOIN metadata_claims mc ON mc.entity_id = ma_mc.id WHERE e_mc.work_id = w.id) {dir}",
            _ => $"ORDER BY (SELECT MIN(mc.claimed_at) FROM editions e_mc INNER JOIN media_assets ma_mc ON ma_mc.edition_id = e_mc.id INNER JOIN metadata_claims mc ON mc.entity_id = ma_mc.id WHERE e_mc.work_id = w.id) {dir}",
        };
    }
}
