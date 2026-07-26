using MediaEngine.Domain.Enums;

namespace MediaEngine.Domain.Services;

/// <summary>
/// Canonical string-to-<see cref="MediaType"/> alias resolution. Unions the
/// alias tables previously duplicated — with subtly different coverage — across
/// six call sites in Providers, Api, Ingestion, and AI. See the stage-4
/// shared-primitives report for the full site list.
/// </summary>
/// <remarks>
/// This complements, and currently overlaps with, <see cref="MediaTypeClassifier"/>
/// in this same namespace. <see cref="MediaTypeClassifier.Classify"/> is aimed at
/// classifying file formats/extensions during ingestion (it recognizes substrings
/// like "mkv" or "flac"); <see cref="Parse"/> is aimed at resolving an explicit
/// media-type string or short alias (query parameters, stored config values,
/// claim values) and does not attempt extension/format sniffing. The two give
/// different answers for some inputs (e.g. <c>Classify("show")</c> is
/// <see cref="MediaType.Unknown"/> while <c>Parse("show")</c> is
/// <see cref="MediaType.TV"/>) — reconciling or merging them is out of scope for
/// this change and left for a later wave to decide.
/// </remarks>
public static class MediaTypeParser
{
    private static readonly Dictionary<string, MediaType> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["unknown"] = MediaType.Unknown,
        ["movies"] = MediaType.Movies,
        ["movie"] = MediaType.Movies,
        ["books"] = MediaType.Books,
        ["book"] = MediaType.Books,
        ["epub"] = MediaType.Books,
        ["ebook"] = MediaType.Books,
        ["audiobooks"] = MediaType.Audiobooks,
        ["audiobook"] = MediaType.Audiobooks,
        ["comics"] = MediaType.Comics,
        ["comic"] = MediaType.Comics,
        ["tv"] = MediaType.TV,
        ["show"] = MediaType.TV,
        ["shows"] = MediaType.TV,
        ["tv show"] = MediaType.TV,
        ["tv shows"] = MediaType.TV,
        ["music"] = MediaType.Music,
    };

    /// <summary>
    /// Parses a media-type string (enum name or known alias, case-insensitive,
    /// whitespace-trimmed) into a <see cref="MediaType"/>. Returns
    /// <see cref="MediaType.Unknown"/> for <c>null</c>, blank, or unrecognized input.
    /// </summary>
    public static MediaType Parse(string? value)
        => TryParse(value, out var mediaType) ? mediaType : MediaType.Unknown;

    /// <summary>
    /// Attempts to parse a media-type string (enum name or known alias,
    /// case-insensitive, whitespace-trimmed) into a <see cref="MediaType"/>.
    /// Returns <c>false</c> for <c>null</c>, blank, or unrecognized input, in
    /// which case <paramref name="mediaType"/> is set to
    /// <see cref="MediaType.Unknown"/>.
    /// </summary>
    public static bool TryParse(string? value, out MediaType mediaType)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            mediaType = MediaType.Unknown;
            return false;
        }

        var trimmed = value.Trim();

        if (Aliases.TryGetValue(trimmed, out mediaType))
            return true;

        if (Enum.TryParse(trimmed, ignoreCase: true, out mediaType))
            return true;

        mediaType = MediaType.Unknown;
        return false;
    }
}
