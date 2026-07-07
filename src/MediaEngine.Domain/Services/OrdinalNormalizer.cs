using System.Globalization;
using System.Text.RegularExpressions;
using MediaEngine.Domain.Enums;

namespace MediaEngine.Domain.Services;

/// <summary>
/// Normalizes sequence positions without media-title special cases.
/// </summary>
public static partial class OrdinalNormalizer
{
    public static OrdinalNormalizationResult Normalize(
        string? raw,
        int? discNumber = null,
        int? trackNumber = null)
    {
        if (discNumber is { } disc && trackNumber is { } track)
        {
            var display = disc > 1 ? $"Disc {disc}, Track {track}" : track.ToString(CultureInfo.InvariantCulture);
            var sort = disc <= 1 ? track : disc * 1000d + track;
            return new OrdinalNormalizationResult(display, sort, null);
        }

        if (string.IsNullOrWhiteSpace(raw))
            return new OrdinalNormalizationResult(null, null, null);

        var trimmed = raw.Trim();
        var format = InferFormat(trimmed);

        var annual = AnnualPattern().Match(trimmed);
        if (annual.Success && TryParseNumber(annual.Groups["value"].Value, out var annualSort))
            return new OrdinalNormalizationResult(annual.Groups["value"].Value, 10000d + annualSort, SequenceFormat.Annual);

        var fractionMatch = FractionPattern().Match(trimmed);
        if (fractionMatch.Success)
        {
            var whole = fractionMatch.Groups["whole"].Success
                && double.TryParse(fractionMatch.Groups["whole"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var wholeValue)
                    ? wholeValue
                    : 0d;
            var sort = whole + 0.5d;
            return new OrdinalNormalizationResult(fractionMatch.Value, sort, format);
        }

        var numberMatch = NumberPattern().Match(trimmed);
        if (numberMatch.Success && TryParseNumber(numberMatch.Groups["value"].Value, out var sortValue))
            return new OrdinalNormalizationResult(numberMatch.Groups["value"].Value.TrimStart('#'), sortValue, format);

        return new OrdinalNormalizationResult(trimmed, null, format);
    }

    public static OrdinalNormalizationResult NormalizeDiscTrack(string? discNumber, string? trackNumber)
    {
        var disc = ParseLeadingInt(discNumber) ?? 1;
        var track = ParseLeadingInt(trackNumber);
        return track is null
            ? new OrdinalNormalizationResult(null, null, null)
            : Normalize(null, disc, track);
    }

    public static int? IntegerOrdinal(double? ordinalSort)
    {
        if (ordinalSort is null)
            return null;

        var rounded = Math.Round(ordinalSort.Value, MidpointRounding.AwayFromZero);
        return Math.Abs(ordinalSort.Value - rounded) < 0.0001d
            ? (int)rounded
            : null;
    }

    private static SequenceFormat? InferFormat(string value)
    {
        if (value.Contains("annual", StringComparison.OrdinalIgnoreCase))
            return SequenceFormat.Annual;
        if (value.Contains("special", StringComparison.OrdinalIgnoreCase))
            return SequenceFormat.Special;
        if (value.Contains("one-shot", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("one shot", StringComparison.OrdinalIgnoreCase))
            return SequenceFormat.OneShot;
        return null;
    }

    private static bool TryParseNumber(string value, out double result)
        => double.TryParse(
            value.TrimStart('#'),
            NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite,
            CultureInfo.InvariantCulture,
            out result);

    private static int? ParseLeadingInt(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var match = LeadingIntPattern().Match(raw);
        return match.Success && int.TryParse(match.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    [GeneratedRegex(@"annual\s*#?\s*(?<value>\d+(?:\.\d+)?)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AnnualPattern();

    [GeneratedRegex(@"(?:(?<whole>\d+)\s+)?1\s*/\s*2", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FractionPattern();

    [GeneratedRegex(@"#?\s*(?<value>\d+(?:\.\d+)?)", RegexOptions.CultureInvariant)]
    private static partial Regex NumberPattern();

    [GeneratedRegex(@"\d+", RegexOptions.CultureInvariant)]
    private static partial Regex LeadingIntPattern();
}

public sealed record OrdinalNormalizationResult(
    string? DisplayValue,
    double? SortValue,
    SequenceFormat? FormatIndicator);
