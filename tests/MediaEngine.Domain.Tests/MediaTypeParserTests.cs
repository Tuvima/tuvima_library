using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Services;

namespace MediaEngine.Domain.Tests;

public sealed class MediaTypeParserTests
{
    [Theory]
    // Exact enum names, case-insensitive.
    [InlineData("Movies", MediaType.Movies)]
    [InlineData("movies", MediaType.Movies)]
    [InlineData("Books", MediaType.Books)]
    [InlineData("Audiobooks", MediaType.Audiobooks)]
    [InlineData("Comics", MediaType.Comics)]
    [InlineData("TV", MediaType.TV)]
    [InlineData("tv", MediaType.TV)]
    [InlineData("Music", MediaType.Music)]
    // Singular aliases folding to their plural enum target.
    [InlineData("Movie", MediaType.Movies)]
    [InlineData("movie", MediaType.Movies)]
    [InlineData("Book", MediaType.Books)]
    [InlineData("Audiobook", MediaType.Audiobooks)]
    [InlineData("Comic", MediaType.Comics)]
    // Format-name aliases folding to Books.
    [InlineData("epub", MediaType.Books)]
    [InlineData("Epub", MediaType.Books)]
    [InlineData("ebook", MediaType.Books)]
    [InlineData("Ebook", MediaType.Books)]
    // TV show aliases.
    [InlineData("show", MediaType.TV)]
    [InlineData("Show", MediaType.TV)]
    [InlineData("shows", MediaType.TV)]
    [InlineData("Shows", MediaType.TV)]
    [InlineData("tv show", MediaType.TV)]
    [InlineData("TV Show", MediaType.TV)]
    [InlineData("tv shows", MediaType.TV)]
    // Whitespace tolerance.
    [InlineData("  Books  ", MediaType.Books)]
    public void Parse_ResolvesKnownAliases(string input, MediaType expected)
    {
        Assert.Equal(expected, MediaTypeParser.Parse(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("podcast")]
    [InlineData("unrecognized-media-type")]
    public void Parse_ReturnsUnknown_ForUnrecognizedOrBlankInput(string? input)
    {
        Assert.Equal(MediaType.Unknown, MediaTypeParser.Parse(input));
    }

    [Fact]
    public void Parse_ReturnsUnknown_ForExplicitUnknownAlias()
    {
        Assert.Equal(MediaType.Unknown, MediaTypeParser.Parse("Unknown"));
    }

    [Fact]
    public void TryParse_ReturnsTrue_ForExplicitUnknownAlias()
    {
        var found = MediaTypeParser.TryParse("Unknown", out var mediaType);

        Assert.True(found);
        Assert.Equal(MediaType.Unknown, mediaType);
    }

    [Fact]
    public void TryParse_ReturnsFalse_ForUnrecognizedInput()
    {
        var found = MediaTypeParser.TryParse("podcast", out var mediaType);

        Assert.False(found);
        Assert.Equal(MediaType.Unknown, mediaType);
    }

    [Fact]
    public void TryParse_ReturnsFalse_ForNullInput()
    {
        var found = MediaTypeParser.TryParse(null, out var mediaType);

        Assert.False(found);
        Assert.Equal(MediaType.Unknown, mediaType);
    }
}
