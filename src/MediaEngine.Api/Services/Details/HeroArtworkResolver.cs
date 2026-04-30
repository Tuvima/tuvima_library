using MediaEngine.Contracts.Details;

namespace MediaEngine.Api.Services.Details;

public static class HeroArtworkResolver
{
    public static HeroArtworkViewModel Resolve(
        DetailEntityType entityType,
        string? backdropUrl,
        string? bannerUrl,
        string? coverUrl,
        string? posterUrl,
        string? portraitUrl,
        string? characterImageUrl,
        IReadOnlyList<string> relatedArtworkUrls)
    {
        var background = FirstNonBlank(backdropUrl, bannerUrl);
        if (!string.IsNullOrWhiteSpace(background))
        {
            return new HeroArtworkViewModel
            {
                Url = background,
                Mode = HeroArtworkMode.Background,
                HasImage = true,
                AspectRatio = 16d / 9d,
                BackgroundPosition = "center right",
                MobilePosition = "center top",
            };
        }

        var cover = FirstNonBlank(
            posterUrl,
            coverUrl,
            portraitUrl,
            characterImageUrl,
            relatedArtworkUrls.FirstOrDefault());

        if (!string.IsNullOrWhiteSpace(cover))
        {
            return new HeroArtworkViewModel
            {
                Url = cover,
                Mode = HeroArtworkMode.CoverFallback,
                HasImage = true,
                AspectRatio = ResolveCoverAspectRatio(entityType),
                BackgroundPosition = "center",
                MobilePosition = "center top",
            };
        }

        return new HeroArtworkViewModel
        {
            Mode = HeroArtworkMode.Placeholder,
            HasImage = false,
            BackgroundPosition = "center",
            MobilePosition = "center",
        };
    }

    private static double ResolveCoverAspectRatio(DetailEntityType entityType) => entityType switch
    {
        DetailEntityType.MusicAlbum => 1d,
        DetailEntityType.Movie or DetailEntityType.MovieSeries or DetailEntityType.TvShow or DetailEntityType.TvSeason or DetailEntityType.TvEpisode => 2d / 3d,
        DetailEntityType.Person or DetailEntityType.MusicArtist or DetailEntityType.Character => 3d / 4d,
        _ => 2d / 3d,
    };

    private static string? FirstNonBlank(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
}
