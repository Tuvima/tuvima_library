using System.Globalization;
using System.Text;

namespace MediaEngine.Providers.Services;

internal static class RetailTextSimilarity
{
    public static double ComputeWordOverlap(string a, string b)
    {
        var aWords = Tokenize(a);
        var bWords = Tokenize(b);

        if (aWords.Count == 0 || bWords.Count == 0)
            return 0.0;

        var coverage = (double)aWords.Count(w => bWords.Contains(w)) / aWords.Count;
        var precision = (double)bWords.Count(w => aWords.Contains(w)) / bWords.Count;

        if (coverage + precision == 0)
            return 0.0;

        return 2 * coverage * precision / (coverage + precision);
    }

    public static bool AreEquivalentNames(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            return false;

        return string.Equals(
            NormalizeComparableText(left),
            NormalizeComparableText(right),
            StringComparison.OrdinalIgnoreCase);
    }

    public static HashSet<string> Tokenize(string text)
    {
        return [.. StripDiacritics(text).ToLowerInvariant()
            .Split([' ', ',', '.', '-', ':', ';', '\'', '"', '(', ')', '[', ']'],
                   StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length >= 2)];
    }

    public static string StripDiacritics(string text)
    {
        var normalized = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    private static string NormalizeComparableText(string text)
    {
        var chars = StripDiacritics(text)
            .Replace("&", " and ", StringComparison.Ordinal)
            .ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : ' ')
            .ToArray();

        return string.Join(' ', new string(chars)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
