using System.Globalization;

namespace MediaEngine.Domain.Services;

/// <summary>
/// Resolves the date that identifies the original creative work while keeping
/// edition, reissue, and retail dates available as separate metadata.
/// </summary>
public static class MediaDateSemantics
{
    public static string? ResolveOriginalYear(
        string? mediaType,
        Func<string, string?> valueForKey)
    {
        ArgumentNullException.ThrowIfNull(valueForKey);

        foreach (var key in OriginalYearKeys(mediaType))
        {
            if (TryExtractYear(valueForKey(key), out var year))
            {
                return year.ToString("0000", CultureInfo.InvariantCulture);
            }
        }

        return null;
    }

    public static string? ResolveExplicitOriginalYear(
        string? mediaType,
        Func<string, string?> valueForKey)
    {
        ArgumentNullException.ThrowIfNull(valueForKey);

        foreach (var key in ExplicitOriginalYearKeys(mediaType))
        {
            if (TryExtractYear(valueForKey(key), out var year))
            {
                return year.ToString("0000", CultureInfo.InvariantCulture);
            }
        }

        return null;
    }

    public static string? ResolveOriginalDate(
        string? mediaType,
        Func<string, string?> valueForKey)
    {
        ArgumentNullException.ThrowIfNull(valueForKey);

        foreach (var key in OriginalDateKeys(mediaType))
        {
            var value = valueForKey(key)?.Trim();
            if (TryExtractYear(value, out _))
            {
                return value;
            }
        }

        return ResolveOriginalYear(mediaType, valueForKey);
    }

    public static string? ResolveEditionYear(Func<string, string?> valueForKey)
    {
        ArgumentNullException.ThrowIfNull(valueForKey);

        foreach (var key in new[]
                 {
                     MetadataFieldConstants.EditionReleaseYear,
                     MetadataFieldConstants.EditionReleaseDate,
                     "release_date",
                     MetadataFieldConstants.Year,
                 })
        {
            if (TryExtractYear(valueForKey(key), out var year))
            {
                return year.ToString("0000", CultureInfo.InvariantCulture);
            }
        }

        return null;
    }

    public static IReadOnlyList<string> OriginalYearKeys(string? mediaType)
    {
        var normalized = mediaType?.Trim().ToLowerInvariant() ?? string.Empty;
        if (normalized.Contains("book", StringComparison.Ordinal)
            || normalized.Contains("audio", StringComparison.Ordinal))
        {
            return
            [
                MetadataFieldConstants.OriginalPublicationYear,
                MetadataFieldConstants.OriginalPublicationDate,
                MetadataFieldConstants.OriginalReleaseYear,
                MetadataFieldConstants.OriginalReleaseDate,
                MetadataFieldConstants.PublicationYear,
                MetadataFieldConstants.PublicationDate,
                "date",
                "release_year",
                MetadataFieldConstants.Year,
                MetadataFieldConstants.EditionReleaseYear,
                MetadataFieldConstants.EditionReleaseDate,
                "release_date",
            ];
        }

        if (normalized.Contains("movie", StringComparison.Ordinal)
            || normalized.Contains("film", StringComparison.Ordinal))
        {
            return
            [
                MetadataFieldConstants.OriginalReleaseYear,
                MetadataFieldConstants.OriginalReleaseDate,
                "premiere_year",
                "premiere_date",
                MetadataFieldConstants.Year,
                "release_date",
                "release_year",
                "date",
                MetadataFieldConstants.EditionReleaseYear,
                MetadataFieldConstants.EditionReleaseDate,
            ];
        }

        if (normalized.Contains("tv", StringComparison.Ordinal)
            || normalized.Contains("television", StringComparison.Ordinal))
        {
            return
            [
                "original_air_year",
                "original_air_date",
                "air_date",
                "first_air_year",
                "first_air_date",
                "premiere_year",
                "premiere_date",
                MetadataFieldConstants.OriginalReleaseYear,
                MetadataFieldConstants.OriginalReleaseDate,
                MetadataFieldConstants.Year,
                "release_year",
                "release_date",
                "date",
                MetadataFieldConstants.EditionReleaseYear,
                MetadataFieldConstants.EditionReleaseDate,
            ];
        }

        if (normalized.Contains("comic", StringComparison.Ordinal)
            || normalized.Contains("manga", StringComparison.Ordinal))
        {
            return
            [
                MetadataFieldConstants.OriginalPublicationYear,
                MetadataFieldConstants.OriginalPublicationDate,
                MetadataFieldConstants.PublicationYear,
                MetadataFieldConstants.PublicationDate,
                "cover_year",
                "cover_date",
                MetadataFieldConstants.Year,
                MetadataFieldConstants.OriginalReleaseYear,
                MetadataFieldConstants.OriginalReleaseDate,
                "release_year",
                "release_date",
                "date",
                MetadataFieldConstants.EditionReleaseYear,
                MetadataFieldConstants.EditionReleaseDate,
            ];
        }

        if (normalized.Contains("music", StringComparison.Ordinal)
            || normalized.Contains("album", StringComparison.Ordinal)
            || normalized.Contains("song", StringComparison.Ordinal))
        {
            return
            [
                MetadataFieldConstants.OriginalReleaseYear,
                MetadataFieldConstants.OriginalReleaseDate,
                "album_release_year",
                "album_release_date",
                MetadataFieldConstants.Year,
                "release_year",
                "release_date",
                "date",
                MetadataFieldConstants.EditionReleaseYear,
                MetadataFieldConstants.EditionReleaseDate,
            ];
        }

        return
        [
            MetadataFieldConstants.OriginalReleaseYear,
            MetadataFieldConstants.OriginalReleaseDate,
            MetadataFieldConstants.OriginalPublicationYear,
            MetadataFieldConstants.OriginalPublicationDate,
            MetadataFieldConstants.PublicationYear,
            MetadataFieldConstants.PublicationDate,
            MetadataFieldConstants.Year,
            "release_year",
            "release_date",
            "date",
            MetadataFieldConstants.EditionReleaseYear,
            MetadataFieldConstants.EditionReleaseDate,
        ];
    }

    public static IReadOnlyList<string> ExplicitOriginalYearKeys(string? mediaType)
    {
        var normalized = mediaType?.Trim().ToLowerInvariant() ?? string.Empty;
        if (normalized.Contains("book", StringComparison.Ordinal)
            || normalized.Contains("audio", StringComparison.Ordinal))
        {
            return
            [
                MetadataFieldConstants.OriginalPublicationYear,
                MetadataFieldConstants.OriginalPublicationDate,
                MetadataFieldConstants.OriginalReleaseYear,
                MetadataFieldConstants.OriginalReleaseDate,
                MetadataFieldConstants.PublicationYear,
                MetadataFieldConstants.PublicationDate,
            ];
        }

        if (normalized.Contains("movie", StringComparison.Ordinal)
            || normalized.Contains("film", StringComparison.Ordinal))
        {
            return
            [
                MetadataFieldConstants.OriginalReleaseYear,
                MetadataFieldConstants.OriginalReleaseDate,
                "premiere_year",
                "premiere_date",
            ];
        }

        if (normalized.Contains("tv", StringComparison.Ordinal)
            || normalized.Contains("television", StringComparison.Ordinal))
        {
            return
            [
                "original_air_year",
                "original_air_date",
                "first_air_year",
                "first_air_date",
                "premiere_year",
                "premiere_date",
                MetadataFieldConstants.OriginalReleaseYear,
                MetadataFieldConstants.OriginalReleaseDate,
            ];
        }

        if (normalized.Contains("comic", StringComparison.Ordinal)
            || normalized.Contains("manga", StringComparison.Ordinal))
        {
            return
            [
                MetadataFieldConstants.OriginalPublicationYear,
                MetadataFieldConstants.OriginalPublicationDate,
                MetadataFieldConstants.PublicationYear,
                MetadataFieldConstants.PublicationDate,
                "cover_year",
                "cover_date",
                MetadataFieldConstants.OriginalReleaseYear,
                MetadataFieldConstants.OriginalReleaseDate,
            ];
        }

        if (normalized.Contains("music", StringComparison.Ordinal)
            || normalized.Contains("album", StringComparison.Ordinal)
            || normalized.Contains("song", StringComparison.Ordinal))
        {
            return
            [
                MetadataFieldConstants.OriginalReleaseYear,
                MetadataFieldConstants.OriginalReleaseDate,
                "album_release_year",
                "album_release_date",
            ];
        }

        return
        [
            MetadataFieldConstants.OriginalReleaseYear,
            MetadataFieldConstants.OriginalReleaseDate,
            MetadataFieldConstants.OriginalPublicationYear,
            MetadataFieldConstants.OriginalPublicationDate,
            MetadataFieldConstants.PublicationYear,
            MetadataFieldConstants.PublicationDate,
        ];
    }

    private static IReadOnlyList<string> OriginalDateKeys(string? mediaType)
    {
        var normalized = mediaType?.Trim().ToLowerInvariant() ?? string.Empty;
        if (normalized.Contains("book", StringComparison.Ordinal)
            || normalized.Contains("audio", StringComparison.Ordinal))
        {
            return
            [
                MetadataFieldConstants.OriginalPublicationDate,
                MetadataFieldConstants.OriginalReleaseDate,
                MetadataFieldConstants.PublicationDate,
                "date",
            ];
        }

        if (normalized.Contains("movie", StringComparison.Ordinal)
            || normalized.Contains("film", StringComparison.Ordinal))
        {
            return
            [
                MetadataFieldConstants.OriginalReleaseDate,
                "premiere_date",
            ];
        }

        if (normalized.Contains("tv", StringComparison.Ordinal)
            || normalized.Contains("television", StringComparison.Ordinal))
        {
            return
            [
                "original_air_date",
                "first_air_date",
                "premiere_date",
                MetadataFieldConstants.OriginalReleaseDate,
                "air_date",
            ];
        }

        if (normalized.Contains("comic", StringComparison.Ordinal)
            || normalized.Contains("manga", StringComparison.Ordinal))
        {
            return
            [
                MetadataFieldConstants.OriginalPublicationDate,
                MetadataFieldConstants.PublicationDate,
                "cover_date",
                MetadataFieldConstants.OriginalReleaseDate,
                "date",
            ];
        }

        if (normalized.Contains("music", StringComparison.Ordinal)
            || normalized.Contains("album", StringComparison.Ordinal)
            || normalized.Contains("song", StringComparison.Ordinal))
        {
            return
            [
                MetadataFieldConstants.OriginalReleaseDate,
                "album_release_date",
            ];
        }

        return
        [
            MetadataFieldConstants.OriginalReleaseDate,
            MetadataFieldConstants.OriginalPublicationDate,
            MetadataFieldConstants.PublicationDate,
        ];
    }

    private static bool TryExtractYear(string? value, out int year)
    {
        year = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim().TrimStart('+');
        if (trimmed.Length < 4
            || !int.TryParse(trimmed[..4], NumberStyles.None, CultureInfo.InvariantCulture, out year))
        {
            return false;
        }

        return year is >= 1 and <= 9999;
    }
}
