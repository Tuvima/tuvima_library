using MediaEngine.Web.Components.Shared;

namespace MediaEngine.Web.Tests;

public sealed class AppPresentationTests
{
    [Theory]
    [InlineData("Books", AppIcons.Book, "var(--tl-media-books)")]
    [InlineData("Audiobooks", AppIcons.Audiobook, "var(--tl-media-audiobooks)")]
    [InlineData("Movies", AppIcons.Movie, "var(--tl-media-movies)")]
    [InlineData("TV Episodes", AppIcons.Television, "var(--tl-media-tv)")]
    [InlineData("Music", AppIcons.Music, "var(--tl-media-music)")]
    [InlineData("Comics", AppIcons.Comic, "var(--tl-media-comics)")]
    public void MediaPresentation_UsesCanonicalSemanticIconAndAccent(
        string mediaType,
        string expectedIcon,
        string expectedAccent)
    {
        Assert.Equal(expectedIcon, AppMediaPresentation.IconKeyFor(mediaType));
        Assert.Equal(expectedAccent, AppMediaPresentation.AccentFor(mediaType));
    }

    [Theory]
    [InlineData("StorageMaintenanceCompleted", "Maintenance", AppIcons.Maintenance)]
    [InlineData("ReconciliationCompleted", "Metadata", AppIcons.Metadata)]
    [InlineData("FolderCleaned", "Cleanup", AppIcons.Cleanup)]
    [InlineData("ServerStarted", "System", AppIcons.System)]
    [InlineData("FileIngested", "Ingestion", AppIcons.Ingestion)]
    public void ActivityPresentation_ClassifiesOperationalEvents(
        string actionType,
        string expectedLabel,
        string expectedIcon)
    {
        var presentation = AppActivityPresentation.For(actionType);

        Assert.Equal(expectedLabel, presentation.Label);
        Assert.Equal(expectedIcon, presentation.IconKey);
        Assert.False(string.IsNullOrWhiteSpace(presentation.AccentColor));
    }
}
