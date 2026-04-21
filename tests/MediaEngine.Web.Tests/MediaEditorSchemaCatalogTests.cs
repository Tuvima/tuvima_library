using MediaEngine.Web.Services.Editing;

namespace MediaEngine.Web.Tests;

public sealed class MediaEditorSchemaCatalogTests
{
    [Fact]
    public void Resolve_MusicSchema_IncludesLyricsTextareaField()
    {
        var schema = MediaEditorSchemaCatalog.Resolve("Music");

        var lyricsField = schema.Groups
            .SelectMany(group => group.Fields)
            .Single(field => field.Key == "lyrics");

        Assert.Equal("Lyrics", lyricsField.Label);
        Assert.Equal("textarea", lyricsField.InputKind);
        Assert.False(lyricsField.SupportsBatch);
    }
}
