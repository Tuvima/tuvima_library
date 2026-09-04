using MediaEngine.Contracts.Details;
using MediaEngine.Domain;
using MediaEngine.Domain.Constants;
using MediaEngine.Domain.Services;

namespace MediaEngine.Api.Services.Details.Internals;

/// <summary>
/// Builds reusable detail view-model fragments from already-loaded projection data.
/// </summary>
internal static class DetailViewModelBuilder
{
    internal static ArtworkSet BuildArtwork(
        DetailEntityType entityType,
        string? backdropUrl,
        string? bannerUrl,
        string? coverUrl,
        string? posterUrl,
        string? portraitUrl,
        IReadOnlyDictionary<string, string> values,
        IReadOnlyList<string> relatedArtwork,
        int ownedFormatCount,
        string? artworkSource,
        string? logoUrl = null)
    {
        var primary = StringHelpers.FirstNonBlankOr(
            string.Empty,
            GetValue(values, MetadataFieldConstants.ArtworkPrimaryHex),
            GetValue(values, "primary_color"),
            "#C9922E");
        var secondary = StringHelpers.FirstNonBlankOr(
            string.Empty,
            GetValue(values, MetadataFieldConstants.ArtworkSecondaryHex),
            GetValue(values, "secondary_color"),
            "#271A3A");
        var accent = StringHelpers.FirstNonBlankOr(
            string.Empty,
            GetValue(values, MetadataFieldConstants.ArtworkAccentHex),
            GetValue(values, "accent_color"),
            "#4F7DBA");
        var backdropTop = !string.IsNullOrWhiteSpace(backdropUrl)
            ? GetValue(values, "background_primary_hex")
            : null;
        var backdropMiddle = !string.IsNullOrWhiteSpace(backdropUrl)
            ? GetValue(values, "background_secondary_hex")
            : null;
        var backdropBottom = !string.IsNullOrWhiteSpace(backdropUrl)
            ? GetValue(values, "background_accent_hex")
            : null;
        var mode = DetailPresentationPolicy.ResolveArtworkPresentationMode(
            entityType,
            backdropUrl,
            bannerUrl,
            coverUrl,
            posterUrl,
            portraitUrl,
            relatedArtwork.Count,
            ownedFormatCount);
        var characterImageUrl = entityType == DetailEntityType.Character ? portraitUrl : null;
        var resolvedLogoUrl = StringHelpers.FirstNonBlankOr(
            string.Empty,
            logoUrl,
            GetValue(values, "clear_logo_url"),
            GetValue(values, "clear_logo"),
            GetValue(values, "logo_url"),
            GetValue(values, "logo"));
        var heroSmallUrl = StringHelpers.FirstNonBlank(
            GetValue(values, "background_url_s"),
            GetValue(values, "banner_url_s"),
            GetValue(values, "episode_still_url_s"),
            GetValue(values, "poster_url_s"),
            GetValue(values, "cover_url_s"));
        var heroMediumUrl = StringHelpers.FirstNonBlank(
            GetValue(values, "background_url_m"),
            GetValue(values, "banner_url_m"),
            GetValue(values, "episode_still_url_m"),
            GetValue(values, "poster_url_m"),
            GetValue(values, "cover_url_m"));
        var heroLargeUrl = StringHelpers.FirstNonBlank(
            GetValue(values, "background_url_l"),
            GetValue(values, "banner_url_l"),
            GetValue(values, "episode_still_url_l"),
            GetValue(values, "poster_url_l"),
            GetValue(values, "cover_url_l"));
        var heroArtwork = HeroArtworkResolver.Resolve(
            entityType,
            backdropUrl,
            bannerUrl,
            coverUrl,
            posterUrl,
            portraitUrl,
            characterImageUrl,
            relatedArtwork,
            resolvedLogoUrl,
            heroSmallUrl,
            heroMediumUrl,
            heroLargeUrl);

        return new ArtworkSet
        {
            BackdropUrl = backdropUrl,
            BannerUrl = bannerUrl,
            PosterUrl = posterUrl,
            CoverUrl = coverUrl,
            LogoUrl = resolvedLogoUrl,
            PortraitUrl = portraitUrl,
            CharacterImageUrl = characterImageUrl,
            RelatedArtworkUrls = relatedArtwork,
            DominantColors = [primary, secondary, accent],
            PrimaryColor = primary,
            SecondaryColor = secondary,
            AccentColor = accent,
            BackdropLeftTopColor = backdropTop,
            BackdropLeftMiddleColor = backdropMiddle,
            BackdropLeftBottomColor = backdropBottom,
            HeroArtwork = heroArtwork,
            PresentationMode = mode,
            Source = ResolveArtworkSource(artworkSource),
        };
    }

    private static ArtworkSource ResolveArtworkSource(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return ArtworkSource.Generated;
        }

        return source.Contains("user", StringComparison.OrdinalIgnoreCase)
               || source.Contains("manual", StringComparison.OrdinalIgnoreCase)
            ? ArtworkSource.User
            : ArtworkSource.Provider;
    }

    private static string? GetValue(IReadOnlyDictionary<string, string> values, string key)
        => values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;
}
