using System.Text.RegularExpressions;

namespace MediaEngine.Providers.Models;

/// <summary>
/// Registry of named value-transform functions used by both the Wikidata SPARQL
/// adapter and the config-driven universal adapter.
///
/// <para>
/// Transform functions are <b>behaviour</b> — they live in code. Which property
/// uses which transform is <b>data</b> — it lives in the configuration.
/// This separation means new transforms require a code change (rare), but
/// reassigning transforms to different properties is a configuration edit.
/// </para>
///
/// <para>
/// Two overloads: <see cref="Apply(string?, string)"/> for simple transforms
/// (used by Wikidata), and <see cref="Apply(string?, string, string?)"/> for
/// parameterised transforms (used by config-driven adapters).
/// </para>
/// </summary>
public static partial class ValueTransformRegistry
{
    // ── Simple transforms (no args) ──────────────────────────────────────────

    private static readonly IReadOnlyDictionary<string, Func<string, string?>> Transforms =
        new Dictionary<string, Func<string, string?>>(StringComparer.OrdinalIgnoreCase)
        {
            // ── Wikidata transforms (existing) ───────────────────────────────

            // Extract 4-digit year from ISO date (e.g. "1965-06-01T00:00:00Z" → "1965")
            ["year_from_iso"] = raw => raw.Length >= 4 ? raw[..4] : raw,

            // Extract the numeric portion from an ordinal string (e.g. "Book 3" → "3")
            ["numeric_portion"] = ExtractNumericPortion,

            // Strip Wikidata entity URI prefix
            ["strip_entity_uri"] = raw =>
                raw.StartsWith("http://www.wikidata.org/entity/", StringComparison.Ordinal)
                    ? raw[(raw.LastIndexOf('/') + 1)..]
                    : raw,

            // Convert Wikimedia Commons filename to a thumbnail URL
            ["commons_url"] = raw =>
            {
                var commonsName = Uri.EscapeDataString(raw.Replace(' ', '_'));
                return $"https://commons.wikimedia.org/wiki/Special:FilePath/{commonsName}?width=300";
            },

            // ── Config-driven transforms (new) ──────────────────────────────

            // Pass value through unchanged (useful for int→string coercion in JSON)
            ["to_string"] = raw => raw,

            // Remove all HTML tags
            ["strip_html"] = raw => HtmlTagRegex().Replace(raw, string.Empty).Trim(),

            // Join array elements with ", " (default). For JsonArray values, see Apply(name, raw, args).
            ["array_join"] = raw => raw,
        };

    // ── Parameterised transforms (with args) ─────────────────────────────────

    private static readonly IReadOnlyDictionary<string, Func<string, string?, string?>> ArgsTransforms =
        new Dictionary<string, Func<string, string?, string?>>(StringComparer.OrdinalIgnoreCase)
        {
            // Plug value into a URL template (e.g. "https://covers.openlibrary.org/b/id/{value}-L.jpg")
            ["url_template"] = (raw, args) =>
                !string.IsNullOrEmpty(args)
                    ? args.Replace("{value}", raw, StringComparison.Ordinal)
                    : raw,

            // Regex find/replace. Args format: "pattern|replacement" (pipe-delimited)
            ["regex_replace"] = (raw, args) =>
            {
                if (string.IsNullOrEmpty(args))
                    return raw;
                var parts = args.Split('|', 2);
                return parts.Length == 2
                    ? Regex.Replace(raw, parts[0], parts[1])
                    : raw;
            },

            // From a JSON array string representation, prefer 13-char element (ISBN-13)
            ["prefer_isbn13"] = (raw, _) => raw, // Handled specially in ConfigDrivenAdapter

            // Join array elements with a custom separator (args = separator, default ", ")
            ["array_join"] = (raw, args) => raw, // Handled specially in ConfigDrivenAdapter

            // From array of objects, extract named field and join (args = field name)
            ["array_nested_join"] = (raw, args) => raw, // Handled specially in ConfigDrivenAdapter

            // Extract first N characters (args = N as string)
            ["first_n_chars"] = (raw, args) =>
            {
                if (string.IsNullOrEmpty(args) || !int.TryParse(args, out var n) || n <= 0)
                    return raw;
                return raw.Length > n ? raw[..n] : raw;
            },

            // Navigate alternative JSON path if primary was null (args = alt key)
            ["fallback_key"] = (raw, _) => raw, // Handled specially in ConfigDrivenAdapter
        };

    /// <summary>
    /// Apply a named transform to a raw value (no args).
    /// Returns the raw value unchanged if <paramref name="transformName"/> is <c>null</c>
    /// or not found in the registry.
    /// </summary>
    public static string? Apply(string? transformName, string rawValue)
    {
        if (string.IsNullOrEmpty(transformName))
            return rawValue;

        return Transforms.TryGetValue(transformName, out var fn)
            ? fn(rawValue)
            : rawValue;
    }

    /// <summary>
    /// Apply a named transform to a raw value with optional arguments.
    /// Tries the parameterised registry first, then falls back to the simple registry.
    /// </summary>
    public static string? Apply(string? transformName, string rawValue, string? args)
    {
        if (string.IsNullOrEmpty(transformName))
            return rawValue;

        // Try parameterised transforms first.
        if (ArgsTransforms.TryGetValue(transformName, out var argsFn))
            return argsFn(rawValue, args);

        // Fall back to simple transforms.
        if (Transforms.TryGetValue(transformName, out var fn))
            return fn(rawValue);

        return rawValue;
    }

    /// <summary>
    /// Check whether a transform name is recognised by the registry.
    /// </summary>
    public static bool IsKnown(string? transformName)
    {
        if (string.IsNullOrEmpty(transformName))
            return false;

        return Transforms.ContainsKey(transformName) || ArgsTransforms.ContainsKey(transformName);
    }

    // ── Private Helpers ──────────────────────────────────────────────────────

    /// <summary>Extract the first contiguous numeric portion (with optional decimal point).</summary>
    private static string? ExtractNumericPortion(string value)
    {
        var start = -1;
        for (var i = 0; i < value.Length; i++)
        {
            if (char.IsDigit(value[i]))
            {
                if (start < 0) start = i;
            }
            else if (value[i] == '.' && start >= 0)
            {
                // Allow decimal within number
            }
            else if (start >= 0)
            {
                return value[start..i];
            }
        }

        return start >= 0 ? value[start..] : value;
    }

    [GeneratedRegex("<[^>]+>", RegexOptions.Compiled)]
    private static partial Regex HtmlTagRegex();
}
