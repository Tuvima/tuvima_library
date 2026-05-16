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

    [Theory]
    [InlineData("Movies")]
    [InlineData("TV")]
    [InlineData("Books")]
    [InlineData("Audiobooks")]
    [InlineData("Comics")]
    [InlineData("Music")]
    public void Resolve_IdentitySchemas_ShowDescriptionOnDetailsTab(string mediaType)
    {
        var schema = MediaEditorSchemaCatalog.Resolve(mediaType);

        var details = schema.Groups.Single(group => group.TabId == "details");
        var options = schema.Groups.Single(group => group.TabId == "options");

        var description = details.Fields.Single(field => field.Key == "description");

        Assert.Equal("Description", description.Label);
        Assert.Equal("textarea", description.InputKind);
        Assert.True(description.IdentityField);
        Assert.DoesNotContain(options.Fields, field => field.Key == "description");
    }

    [Theory]
    [InlineData("Books", "title", "year", "description")]
    [InlineData("Audiobooks", "title", "year", "description")]
    [InlineData("Movies", "title", "year", "description")]
    [InlineData("TV", "show_name", "episode_title", "description")]
    [InlineData("Music", "title", "year", "description")]
    [InlineData("Comics", "title", "year", "description")]
    public void Resolve_DetailsFields_StartWithCoreIdentityBlock(string mediaType, params string[] expectedKeys)
    {
        var schema = MediaEditorSchemaCatalog.Resolve(mediaType);

        var actualKeys = schema.Groups
            .Single(group => group.TabId == "details")
            .Fields
            .Take(expectedKeys.Length)
            .Select(field => field.Key)
            .ToArray();

        Assert.Equal(expectedKeys, actualKeys);
    }
}
