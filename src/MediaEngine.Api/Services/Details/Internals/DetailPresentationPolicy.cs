using MediaEngine.Contracts.Details;

namespace MediaEngine.Api.Services.Details.Internals;

internal static class DetailPresentationPolicy
{
    internal static bool TryParseEntityType(string value, out DetailEntityType entityType)
    {
        entityType = default;
        if (value.Contains("podcast", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var normalized = value.Replace("-", string.Empty).Replace("_", string.Empty);
        return Enum.TryParse(normalized, ignoreCase: true, out entityType);
    }

    internal static DetailPresentationContext ParseContext(string? value)
        => Enum.TryParse<DetailPresentationContext>(value, ignoreCase: true, out var parsed)
            ? parsed
            : DetailPresentationContext.Default;

    internal static ArtworkPresentationMode ResolveArtworkPresentationMode(
        DetailEntityType entityType,
        string? backdropUrl,
        string? bannerUrl,
        string? coverUrl,
        string? posterUrl,
        string? portraitUrl,
        int relatedArtworkCount,
        int ownedFormatCount)
    {
        if (ownedFormatCount > 1
            && entityType is DetailEntityType.Work or DetailEntityType.Book or DetailEntityType.Audiobook)
        {
            return ArtworkPresentationMode.PairedEditionGradient;
        }

        if (!string.IsNullOrWhiteSpace(backdropUrl) || !string.IsNullOrWhiteSpace(bannerUrl))
        {
            return ArtworkPresentationMode.CinematicBackdrop;
        }

        if (entityType == DetailEntityType.Person && !string.IsNullOrWhiteSpace(portraitUrl))
        {
            return ArtworkPresentationMode.PortraitEcho;
        }

        if (!string.IsNullOrWhiteSpace(coverUrl) || !string.IsNullOrWhiteSpace(posterUrl))
        {
            return ArtworkPresentationMode.ColorGradientFromArtwork;
        }

        return relatedArtworkCount > 1
            ? ArtworkPresentationMode.CollageGradient
            : ArtworkPresentationMode.GeneratedIdentity;
    }
}
