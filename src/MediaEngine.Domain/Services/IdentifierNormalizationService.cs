using System.Text.RegularExpressions;

namespace MediaEngine.Domain.Services;

/// <summary>
/// Static utility for normalizing media identifiers (ISBNs, ASINs, IMDb IDs, etc.)
/// between different formats. Lives in MediaEngine.Domain so both Providers and
/// Processors can reference it without introducing circular dependencies.
/// </summary>
public static class IdentifierNormalizationService
{
    /// <summary>
    /// Cleans up raw input from any source (file metadata, user input, APIs).
    /// Returns null if the value is invalid or unrecognizable for the given property code.
    /// For unrecognized property codes, returns the trimmed input unchanged.
    /// </summary>
    public static string? NormalizeRaw(string propertyCode, string rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
            return null;

        return propertyCode switch
        {
            "P212"  => NormalizeIsbn13(rawValue),
            "P957"  => NormalizeIsbn10(rawValue),
            "P5749" => NormalizeAsin(rawValue),
            "P345"  => NormalizeImdbId(rawValue),
            "P3861" => NormalizeAppleBooksId(rawValue),
            "P4947" => NormalizeTmdbId(rawValue),
            "P434"  => NormalizeMusicBrainzId(rawValue),
            "P2969" => NormalizeGoodreadsId(rawValue),
            "P5905" => NormalizeComicVineId(rawValue),
            "P1243" => NormalizeIsrc(rawValue),
            "P244"  => NormalizeLccn(rawValue),
            "P5842" => NormalizeApplePodcastsId(rawValue),
            _       => rawValue.Trim()
        };
    }

    /// <summary>
    /// Converts the identifier to Wikidata's expected format.
    /// Returns null if the value is invalid or unrecognizable for the given property code.
    /// For unrecognized property codes, returns the trimmed input unchanged.
    /// </summary>
    public static string? ToWikidataFormat(string propertyCode, string rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
            return null;

        // For all currently supported identifiers, Wikidata format is the same as NormalizeRaw.
        return NormalizeRaw(propertyCode, rawValue);
    }

    /// <summary>
    /// Converts the identifier to a retail/API-ready format (for Apple, TMDB, etc.).
    /// Returns null if the value is invalid or unrecognizable for the given property code.
    /// For unrecognized property codes, returns the trimmed input unchanged.
    /// </summary>
    public static string? ToRetailFormat(string propertyCode, string rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
            return null;

        // For all currently supported identifiers, retail format is the same as NormalizeRaw.
        return NormalizeRaw(propertyCode, rawValue);
    }

    /// <summary>
    /// Returns the canonical claim key alias for a given claim key.
    /// For example, "isbn_13" and "isbn_10" both alias to "isbn".
    /// Returns null if no alias exists.
    /// </summary>
    public static string? GetClaimKeyAlias(string claimKey)
    {
        return claimKey switch
        {
            "isbn_13" => "isbn",
            "isbn_10" => "isbn",
            _         => null
        };
    }

    /// <summary>
    /// Normalizes the app-level language code for a specific provider's expected format.
    /// Different providers use different language code formats:
    /// - Wikidata, Wikipedia, Google Books use ISO 639-1 two-letter codes ("en", "es", "fr").
    /// - Apple API uses locale codes with underscore ("en_us", "es_mx").
    /// </summary>
    /// <param name="appLanguage">The configured app language (e.g. "en", "en-US", "es").</param>
    /// <param name="providerName">The provider name as declared in its config file.</param>
    /// <returns>The language code in the format expected by the target provider.</returns>
    public static string NormalizeLanguageForProvider(string appLanguage, string providerName)
    {
        // Extract primary subtag (e.g. "en-US" → "en", "zh-Hant" → "zh").
        var primary = appLanguage.Split('-', '_')[0].ToLowerInvariant();

        return providerName.ToLowerInvariant() switch
        {
            // Apple API uses underscore locale codes: "en" → "en_us", "es" → "es_mx", "fr" → "fr_fr".
            "apple_api" or "apple_books" or "apple podcasts" or "apple_podcasts"
                => $"{primary}_{primary}",

            // All other providers (Wikidata, Wikipedia, Google Books, Open Library, etc.)
            // use the standard two-letter ISO 639-1 code.
            _ => primary,
        };
    }

    // -------------------------------------------------------------------------
    #region ISBN-13 (P212)
    // -------------------------------------------------------------------------

    private static string? NormalizeIsbn13(string rawValue)
    {
        // Strip all non-alphanumeric characters.
        var digits = Regex.Replace(rawValue, @"[^0-9]", string.Empty);

        if (digits.Length != 13)
            return null;

        // Mod10 checksum: alternate weights 1 and 3.
        var sum = 0;
        for (var i = 0; i < 12; i++)
        {
            if (!int.TryParse(digits[i].ToString(), out var d))
                return null;

            sum += d * (i % 2 == 0 ? 1 : 3);
        }

        var checkDigit = (10 - (sum % 10)) % 10;

        if (!int.TryParse(digits[12].ToString(), out var lastDigit))
            return null;

        return checkDigit == lastDigit ? digits : null;
    }

    #endregion

    // -------------------------------------------------------------------------
    #region ISBN-10 (P957)
    // -------------------------------------------------------------------------

    private static string? NormalizeIsbn10(string rawValue)
    {
        // Strip all non-alphanumeric chars, preserving trailing 'X'.
        var cleaned = Regex.Replace(rawValue.ToUpperInvariant(), @"[^0-9X]", string.Empty);

        if (cleaned.Length != 10)
            return null;

        // Validate: digits 0–8 must be numeric; position 9 may be 'X' or a digit.
        for (var i = 0; i < 9; i++)
        {
            if (!char.IsDigit(cleaned[i]))
                return null;
        }

        if (!char.IsDigit(cleaned[9]) && cleaned[9] != 'X')
            return null;

        // ISBN-10 checksum: sum of (digit * position from 10 down to 1), mod 11 == 0.
        var sum = 0;
        for (var i = 0; i < 10; i++)
        {
            var c = cleaned[i];
            var value = c == 'X' ? 10 : (c - '0');
            sum += value * (10 - i);
        }

        if (sum % 11 != 0)
        {
            // Check if it looks like an ISSN (4 digits, dash, 4 digits where last may be X).
            // If so, treat it as not an ISBN-10.
            if (Regex.IsMatch(rawValue.Trim(), @"^\d{4}-\d{3}[\dX]$", RegexOptions.IgnoreCase))
                return null;

            return null;
        }

        return cleaned;
    }

    #endregion

    // -------------------------------------------------------------------------
    #region ASIN (P5749)
    // -------------------------------------------------------------------------

    private static string? NormalizeAsin(string rawValue)
    {
        var trimmed = rawValue.Trim().ToUpperInvariant();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    #endregion

    // -------------------------------------------------------------------------
    #region IMDb ID (P345)
    // -------------------------------------------------------------------------

    private static string? NormalizeImdbId(string rawValue)
    {
        // Extract tt\d+ from the input (handles full URLs and bare IDs).
        var match = Regex.Match(rawValue, @"tt\d+", RegexOptions.IgnoreCase);
        return match.Success ? match.Value.ToLowerInvariant() : null;
    }

    #endregion

    // -------------------------------------------------------------------------
    #region Apple Books ID (P3861)
    // -------------------------------------------------------------------------

    private static string? NormalizeAppleBooksId(string rawValue)
    {
        return ExtractAppleNumericId(rawValue);
    }

    #endregion

    // -------------------------------------------------------------------------
    #region TMDB ID (P4947)
    // -------------------------------------------------------------------------

    private static string? NormalizeTmdbId(string rawValue)
    {
        // Extract leading or embedded numeric portion (handles URLs like /movie/123 or bare "123").
        var match = Regex.Match(rawValue, @"\d+");
        return match.Success ? match.Value : null;
    }

    #endregion

    // -------------------------------------------------------------------------
    #region MusicBrainz (P434)
    // -------------------------------------------------------------------------

    private static string? NormalizeMusicBrainzId(string rawValue)
    {
        var cleaned = rawValue.Trim().ToLowerInvariant();

        // Validate UUID format: 8-4-4-4-12 hex chars with dashes.
        if (Regex.IsMatch(cleaned, @"^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"))
            return cleaned;

        return null;
    }

    #endregion

    // -------------------------------------------------------------------------
    #region Goodreads (P2969)
    // -------------------------------------------------------------------------

    private static string? NormalizeGoodreadsId(string rawValue)
    {
        // Extract leading numeric portion; strip any ".Title_Name" or "-title-name" suffix.
        var match = Regex.Match(rawValue.Trim(), @"^(\d+)");
        return match.Success ? match.Groups[1].Value : null;
    }

    #endregion

    // -------------------------------------------------------------------------
    #region ComicVine (P5905)
    // -------------------------------------------------------------------------

    private static string? NormalizeComicVineId(string rawValue)
    {
        var trimmed = rawValue.Trim();

        // Validate format: \d+-\d+ (e.g. "4000-123").
        return Regex.IsMatch(trimmed, @"^\d+-\d+$") ? trimmed : null;
    }

    #endregion

    // -------------------------------------------------------------------------
    #region ISRC (P1243)
    // -------------------------------------------------------------------------

    private static string? NormalizeIsrc(string rawValue)
    {
        // Uppercase, strip dashes and spaces, validate 12 alphanumeric chars.
        var cleaned = Regex.Replace(rawValue.ToUpperInvariant(), @"[\s\-]", string.Empty);

        if (cleaned.Length != 12)
            return null;

        return Regex.IsMatch(cleaned, @"^[A-Z0-9]{12}$") ? cleaned : null;
    }

    #endregion

    // -------------------------------------------------------------------------
    #region LCCN (P244)
    // -------------------------------------------------------------------------

    private static string? NormalizeLccn(string rawValue)
    {
        // Normalize by stripping extra whitespace. Return trimmed result.
        var trimmed = rawValue.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    #endregion

    // -------------------------------------------------------------------------
    #region Apple Podcasts (P5842)
    // -------------------------------------------------------------------------

    private static string? NormalizeApplePodcastsId(string rawValue)
    {
        return ExtractAppleNumericId(rawValue);
    }

    #endregion

    // -------------------------------------------------------------------------
    #region Shared helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Shared logic for Apple IDs: strips "id" prefix and "collection/" prefix,
    /// then extracts the numeric portion.
    /// </summary>
    private static string? ExtractAppleNumericId(string rawValue)
    {
        var value = rawValue.Trim();

        // Strip "collection/" prefix if present.
        var collectionIndex = value.IndexOf("collection/", StringComparison.OrdinalIgnoreCase);
        if (collectionIndex >= 0)
            value = value[(collectionIndex + "collection/".Length)..];

        // Strip leading "id" prefix if present.
        if (value.StartsWith("id", StringComparison.OrdinalIgnoreCase))
            value = value[2..];

        // Extract numeric portion.
        var match = Regex.Match(value, @"\d+");
        return match.Success ? match.Value : null;
    }

    #endregion
}
