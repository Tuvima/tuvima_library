using MediaEngine.Contracts.Details;
using MediaEngine.Domain.Enums;
namespace MediaEngine.Api.Services.Details;

public static class PersonalStatusPolicy
{
    public static MediaType MediaTypeFor(DetailEntityType type) => type switch
    {
        DetailEntityType.Movie or DetailEntityType.MovieSeries => MediaType.Movies,
        DetailEntityType.TvShow or DetailEntityType.TvSeason or DetailEntityType.TvEpisode => MediaType.TV,
        DetailEntityType.Book or DetailEntityType.BookSeries => MediaType.Books,
        DetailEntityType.ComicIssue or DetailEntityType.ComicSeries => MediaType.Comics,
        DetailEntityType.Audiobook => MediaType.Audiobooks,
        DetailEntityType.MusicAlbum => MediaType.Music,
        _ => MediaType.Unknown,
    };
}
