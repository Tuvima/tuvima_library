using MediaEngine.Api.Services.Display;

namespace MediaEngine.Api.Tests;

public sealed class DisplayFactBuilderTests
{
    [Fact]
    public void MovieFacts_UseYearAndUniqueGenres()
    {
        var facts = DisplayFactBuilder.Build(
            mediaKind: "Movie",
            title: "Arrival",
            year: "2016",
            genre: "Science Fiction|||Drama|||Science Fiction");

        Assert.Equal(["2016", "Science Fiction", "Drama"], facts);
    }

    [Fact]
    public void BookFacts_UseAuthorAndGenresWithoutRepeatingTitle()
    {
        var facts = DisplayFactBuilder.Build(
            mediaKind: "Book",
            title: "Dune",
            author: "Frank Herbert",
            series: "Dune",
            genre: "Science Fiction;Adventure");

        Assert.Equal(["Frank Herbert", "Science Fiction", "Adventure"], facts);
    }

    [Fact]
    public void AudiobookFacts_IncludeNarratorWithoutDuplicatingAuthor()
    {
        var facts = DisplayFactBuilder.Build(
            mediaKind: "Audiobook",
            title: "Project Hail Mary",
            author: "Andy Weir",
            narrator: "Ray Porter",
            genre: "Science Fiction");

        Assert.Equal(["Andy Weir", "Narrated by Ray Porter", "Science Fiction"], facts);
    }

    [Fact]
    public void TvFacts_UseEpisodeAndGenre()
    {
        var facts = DisplayFactBuilder.Build(
            mediaKind: "TV",
            title: "Pilot",
            year: "2008",
            genre: "Crime",
            season: "1",
            episode: "1");

        Assert.Equal(["2008", "S1:E1", "Crime"], facts);
    }

    [Fact]
    public void MusicFacts_UseArtistAlbumTrackAndGenre()
    {
        var facts = DisplayFactBuilder.Build(
            mediaKind: "Music",
            title: "Not Strong Enough",
            artist: "boygenius",
            album: "The Record",
            track: "6",
            genre: "Indie Rock");

        Assert.Equal(["boygenius", "The Record", "Track 6", "Indie Rock"], facts);
    }
}
