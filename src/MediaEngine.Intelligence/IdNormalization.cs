using MediaEngine.Domain.Enums;

namespace MediaEngine.Intelligence;

/// <summary>
/// Static utility for normalising media-type-specific unique identifiers.
///
/// Each normalisation method strips formatting noise (dashes, spaces, mixed case)
/// and validates check digits where applicable. Invalid inputs are returned as-is
/// after basic trimming — the caller decides whether to reject or propagate them.
///
/// <list type="bullet">
///   <item><see cref="NormalizeIsbn"/> — ISBN-10 → ISBN-13 promotion, check-digit validation.</item>
///   <item><see cref="NormalizeAsin"/> — Amazon Standard Identification Number trimming.</item>
///   <item><see cref="NormalizeIsrc"/> — International Standard Recording Code (placeholder).</item>
///   <item><see cref="GetPrimaryIdFields"/> — ordered ID field names per media type.</item>
///   <item><see cref="GetFallbackLookupOrder"/> — title → filename fallback chain.</item>
/// </list>
/// </summary>
public static class IdNormalization
{
    // ── ISBN ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Normalises an ISBN string: strips dashes and spaces, converts ISBN-10 to
    /// ISBN-13 (978 prefix), and validates the check digit.
    /// Returns the normalised ISBN-13 when valid, or a trimmed/stripped version
    /// of the original when the input is not a valid ISBN.
    /// </summary>
    public static string NormalizeIsbn(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        // Strip dashes, spaces, and leading/trailing whitespace.
        var stripped = raw.Replace("-", "", StringComparison.Ordinal)
                         .Replace(" ", "", StringComparison.Ordinal)
                         .Trim();

        if (stripped.Length == 10)
        {
            // ISBN-10: validate then promote to ISBN-13.
            if (IsValidIsbn10(stripped))
                return ConvertIsbn10To13(stripped);

            // Invalid check digit — return stripped as-is.
            return stripped;
        }

        if (stripped.Length == 13)
        {
            // ISBN-13: validate check digit.
            if (IsValidIsbn13(stripped))
                return stripped;

            // Invalid check digit — return stripped as-is.
            return stripped;
        }

        // Neither 10 nor 13 digits — not a valid ISBN. Return stripped.
        return stripped;
    }

    /// <summary>
    /// Validates an ISBN-10 check digit.
    /// The check digit is computed as: sum = Σ digit[i] × (10 - i) for i = 0..8.
    /// Check = (11 - (sum mod 11)) mod 11; value 10 is represented as 'X'.
    /// </summary>
    private static bool IsValidIsbn10(string isbn10)
    {
        if (isbn10.Length != 10)
            return false;

        int sum = 0;
        for (int i = 0; i < 9; i++)
        {
            if (!char.IsAsciiDigit(isbn10[i]))
                return false;
            sum += (isbn10[i] - '0') * (10 - i);
        }

        int expected = (11 - (sum % 11)) % 11;
        char checkChar = isbn10[9];

        if (expected == 10)
            return checkChar is 'X' or 'x';

        return char.IsAsciiDigit(checkChar) && (checkChar - '0') == expected;
    }

    /// <summary>
    /// Validates an ISBN-13 check digit.
    /// Weights alternate 1, 3, 1, 3, … for the first 12 digits.
    /// Check = (10 - (sum mod 10)) mod 10.
    /// </summary>
    private static bool IsValidIsbn13(string isbn13)
    {
        if (isbn13.Length != 13)
            return false;

        int sum = 0;
        for (int i = 0; i < 12; i++)
        {
            if (!char.IsAsciiDigit(isbn13[i]))
                return false;
            int weight = (i % 2 == 0) ? 1 : 3;
            sum += (isbn13[i] - '0') * weight;
        }

        int expected = (10 - (sum % 10)) % 10;
        if (!char.IsAsciiDigit(isbn13[12]))
            return false;

        return (isbn13[12] - '0') == expected;
    }

    /// <summary>
    /// Converts a valid ISBN-10 to ISBN-13 by prepending "978" and recomputing
    /// the check digit using ISBN-13 rules.
    /// </summary>
    private static string ConvertIsbn10To13(string isbn10)
    {
        // Take first 9 digits (drop ISBN-10 check digit), prepend "978".
        var partial = "978" + isbn10[..9];

        int sum = 0;
        for (int i = 0; i < 12; i++)
        {
            int weight = (i % 2 == 0) ? 1 : 3;
            sum += (partial[i] - '0') * weight;
        }

        int checkDigit = (10 - (sum % 10)) % 10;
        return partial + checkDigit.ToString();
    }

    // ── ASIN ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Normalises an ASIN: trims whitespace and converts to uppercase.
    /// Amazon ASINs are 10-character alphanumeric identifiers.
    /// </summary>
    public static string NormalizeAsin(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        return raw.Trim().ToUpperInvariant();
    }

    // ── ISRC ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Normalises an ISRC (International Standard Recording Code): strips dashes
    /// and converts to uppercase. Format: CC-XXX-YY-NNNNN (12 chars stripped).
    /// Placeholder for Music media type — full validation deferred.
    /// </summary>
    public static string NormalizeIsrc(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        return raw.Replace("-", "", StringComparison.Ordinal)
                  .Trim()
                  .ToUpperInvariant();
    }

    // ── Media type ID field maps ─────────────────────────────────────────────

    /// <summary>
    /// Returns the ordered list of primary unique-identifier field names for a
    /// given media type. These are the fields checked during Stage 1 content
    /// matching — tried in order, first match wins.
    /// </summary>
    public static IReadOnlyList<string> GetPrimaryIdFields(MediaType type) => type switch
    {
        MediaType.Epub      => ["isbn"],
        MediaType.Audiobook => ["asin", "isbn"],
        MediaType.Music     => ["isrc"],
        MediaType.Movie     => ["tmdb_id", "imdb_id"],
        MediaType.TvShow    => ["tmdb_id", "imdb_id"],
        MediaType.Comic     => ["comicvine_id"],
        _                   => [],
    };

    /// <summary>
    /// Returns the fallback lookup order when no unique ID is available.
    /// Currently: title first, then filename-without-extension.
    /// </summary>
    public static IReadOnlyList<string> GetFallbackLookupOrder(MediaType type)
    {
        // Universal fallback: title → filename (without extension).
        // The caller resolves "filename" from the MediaAsset's file path.
        _ = type; // reserved for future per-type customisation
        return ["title", "filename"];
    }
}
