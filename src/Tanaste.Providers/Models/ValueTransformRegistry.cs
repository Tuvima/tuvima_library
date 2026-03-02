namespace Tanaste.Providers.Models;

/// <summary>
/// Registry of named value-transform functions for SPARQL property results.
///
/// <para>
/// Transform functions are <b>behaviour</b> — they live in code. Which property
/// uses which transform is <b>data</b> — it lives in the universe configuration.
/// This separation means new transforms require a code change (rare), but
/// reassigning transforms to different properties is a configuration edit.
/// </para>
///
/// <para>
/// Each transform takes a raw SPARQL value string and returns the cleaned result.
/// If a transform name is <c>null</c> or unknown, the raw value passes through untouched.
/// </para>
/// </summary>
public static class ValueTransformRegistry
{
    private static readonly IReadOnlyDictionary<string, Func<string, string?>> Transforms =
        new Dictionary<string, Func<string, string?>>(StringComparer.OrdinalIgnoreCase)
        {
            // Extract 4-digit year from ISO date (e.g. "1965-06-01T00:00:00Z" → "1965")
            ["year_from_iso"] = raw => raw.Length >= 4 ? raw[..4] : raw,

            // Extract the numeric portion from an ordinal string (e.g. "Book 3" → "3")
            ["numeric_portion"] = ExtractNumericPortion,

            // Strip Wikidata entity URI prefix (e.g. "http://www.wikidata.org/entity/Q190192" → "Q190192")
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
        };

    /// <summary>
    /// Apply a named transform to a raw value. Returns the raw value unchanged if
    /// <paramref name="transformName"/> is <c>null</c> or not found in the registry.
    /// </summary>
    public static string? Apply(string? transformName, string rawValue)
    {
        if (string.IsNullOrEmpty(transformName))
            return rawValue;

        return Transforms.TryGetValue(transformName, out var fn)
            ? fn(rawValue)
            : rawValue;
    }

    // ── Private Helpers ────────────────────────────────────────────────────

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
}
