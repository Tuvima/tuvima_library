using MediaEngine.Web.Components.Library;

namespace MediaEngine.Web.Tests;

public sealed class LibraryMusicGridTests
{
    [Theory]
    [InlineData(new[] { "225" }, 225L)]
    [InlineData(new[] { "225000" }, 225L)]
    [InlineData(new[] { "bad", "3:45" }, 225L)]
    [InlineData(new[] { "1:01:01" }, 3661L)]
    [InlineData(new[] { null, "", "0" }, 0L)]
    public void NormalizeDurationSeconds_UsesFileStyleDurationInputs(string?[] candidates, long expected)
    {
        var actual = LibraryHelpers.NormalizeDurationSeconds(candidates);

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(225L, null, "3:45")]
    [InlineData(3661L, null, "1:01:01")]
    [InlineData(null, "4:05", "4:05")]
    [InlineData(0L, null, "0:00")]
    [InlineData(null, null, "0:00")]
    public void FormatDuration_ReturnsStableMusicDisplay(long? durationSeconds, string? fallback, string expected)
    {
        var actual = LibraryHelpers.FormatDuration(durationSeconds, fallback);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void MusicColumns_ExposeExpectedDefaultSongGrid()
    {
        var columns = LibraryColumnDefinitions.GetColumnsByTab("music");

        var visibleKeys = columns
            .Where(column => column.DefaultVisible)
            .Select(column => column.Key)
            .ToArray();

        Assert.Equal(
            ["checkbox", "title", "time", "artist", "album", "genre", "favorite", "plays", "date_added"],
            visibleKeys);
    }

    [Fact]
    public void MusicColumns_IncludeFirstWaveAppleMusicStyleFields()
    {
        var columns = LibraryColumnDefinitions.GetColumnsByTab("music");
        var keys = columns.Select(column => column.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains("album_artist", keys);
        Assert.Contains("composer", keys);
        Assert.Contains("comments", keys);
        Assert.Contains("date_modified", keys);
        Assert.Contains("disc_number", keys);
        Assert.Contains("kind", keys);
        Assert.Contains("last_played", keys);
        Assert.Contains("rating", keys);
        Assert.Contains("release_date", keys);
        Assert.Contains("size", keys);
        Assert.Contains("sort_album", keys);
        Assert.Contains("sort_artist", keys);
        Assert.Contains("sort_title", keys);
        Assert.Contains("track_number", keys);
        Assert.Contains("year", keys);
    }

    [Fact]
    public void MusicColumns_UseStableSortKeyForDateAdded()
    {
        var column = LibraryColumnDefinitions
            .GetColumnsByTab("music")
            .Single(definition => definition.Key == "date_added");

        Assert.Equal("dateAdded", column.SortKey);
    }
}
