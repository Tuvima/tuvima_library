using System.Text.Json.Nodes;

namespace Tanaste.Providers.Models;

/// <summary>
/// Simple JSON path evaluator for navigating <see cref="JsonNode"/> trees
/// using dot-notation with array indexing.
///
/// <para>Supported syntax:</para>
/// <list type="bullet">
///   <item><c>title</c> — property access</item>
///   <item><c>author_name[0]</c> — array element access</item>
///   <item><c>volumeInfo.title</c> — nested property access</item>
///   <item><c>narrators[*].name</c> — wildcard: iterate array, extract child from each</item>
/// </list>
///
/// <para>
/// Uses <c>System.Text.Json.Nodes</c> (BCL) — no external dependencies.
/// Returns <c>null</c> on any navigation failure (missing key, wrong type, out-of-bounds).
/// </para>
/// </summary>
public static class JsonPathEvaluator
{
    /// <summary>
    /// Evaluate a dot-notation path against a <see cref="JsonNode"/>.
    /// Returns the matched node, or <c>null</c> if any segment fails.
    /// </summary>
    public static JsonNode? Evaluate(JsonNode? root, string path)
    {
        if (root is null || string.IsNullOrEmpty(path))
            return null;

        var current = root;
        var segments = path.Split('.');

        foreach (var segment in segments)
        {
            if (current is null)
                return null;

            // Check for array access: "property[index]" or "property[*]"
            var bracketStart = segment.IndexOf('[');
            if (bracketStart >= 0)
            {
                // Navigate to the property first (if there's a name before the bracket).
                if (bracketStart > 0)
                {
                    var propName = segment[..bracketStart];
                    current = NavigateProperty(current, propName);
                    if (current is null)
                        return null;
                }

                // Parse the bracket expression.
                var bracketEnd = segment.IndexOf(']', bracketStart);
                if (bracketEnd < 0)
                    return null;

                var indexStr = segment[(bracketStart + 1)..bracketEnd];

                if (indexStr == "*")
                {
                    // Wildcard: extract a child property from each array element.
                    // Remaining path after "]." is the child key.
                    var remaining = bracketEnd + 1 < segment.Length && segment[bracketEnd + 1] == '.'
                        ? segment[(bracketEnd + 2)..]
                        : null;

                    return ExtractWildcard(current, remaining, segments, segment);
                }

                // Numeric index.
                if (!int.TryParse(indexStr, out var index))
                    return null;

                if (current is not JsonArray arr || index < 0 || index >= arr.Count)
                    return null;

                current = arr[index];
            }
            else
            {
                current = NavigateProperty(current, segment);
            }
        }

        return current;
    }

    /// <summary>
    /// Extract a string value from a JSON node. Handles primitives, numbers, and booleans.
    /// Returns <c>null</c> if the node is null, an object, or an array.
    /// </summary>
    public static string? GetStringValue(JsonNode? node)
    {
        if (node is null)
            return null;

        if (node is JsonValue value)
        {
            // Try string first, then fall back to raw text.
            if (value.TryGetValue<string>(out var str))
                return str;
            if (value.TryGetValue<int>(out var intVal))
                return intVal.ToString();
            if (value.TryGetValue<long>(out var longVal))
                return longVal.ToString();
            if (value.TryGetValue<double>(out var dblVal))
                return dblVal.ToString("G");
            if (value.TryGetValue<bool>(out var boolVal))
                return boolVal.ToString();

            return value.ToJsonString().Trim('"');
        }

        // Arrays and objects are not scalar — return null.
        return null;
    }

    /// <summary>
    /// Check if the evaluated node is a JSON array (used by transforms like prefer_isbn13).
    /// </summary>
    public static bool IsArray(JsonNode? node) => node is JsonArray;

    /// <summary>
    /// Extract all string values from a JSON array node.
    /// </summary>
    public static IReadOnlyList<string> GetArrayValues(JsonNode? node)
    {
        if (node is not JsonArray arr)
            return [];

        var result = new List<string>(arr.Count);
        foreach (var item in arr)
        {
            var str = GetStringValue(item);
            if (!string.IsNullOrWhiteSpace(str))
                result.Add(str);
        }

        return result;
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private static JsonNode? NavigateProperty(JsonNode current, string propertyName)
    {
        if (current is JsonObject obj)
            return obj[propertyName];

        return null;
    }

    /// <summary>
    /// Handle wildcard array iteration: <c>narrators[*].name</c>.
    /// Builds a synthetic JSON array of the extracted child values.
    /// </summary>
    private static JsonNode? ExtractWildcard(
        JsonNode current, string? childKey,
        string[] allSegments, string currentSegment)
    {
        if (current is not JsonArray arr)
            return null;

        var results = new JsonArray();
        foreach (var element in arr)
        {
            if (element is null) continue;

            JsonNode? extracted;
            if (!string.IsNullOrEmpty(childKey))
            {
                // Navigate into the child object.
                extracted = Evaluate(element, childKey);
            }
            else
            {
                extracted = element;
            }

            if (extracted is not null)
            {
                var strVal = GetStringValue(extracted);
                if (!string.IsNullOrWhiteSpace(strVal))
                    results.Add(JsonValue.Create(strVal));
            }
        }

        return results.Count > 0 ? results : null;
    }
}
