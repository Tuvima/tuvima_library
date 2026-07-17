using MediaEngine.Web.Services.Editing;

namespace MediaEngine.Web.Tests;

public sealed class MediaEditorSchemaCatalogTests
{
    [Fact]
    public void Resolve_NormalSchemas_OmitProviderManagedPeopleAndTranscriptFields()
    {
        var fields = new[] { "Movies", "TV", "Books", "Audiobooks", "Comics", "Music" }
            .SelectMany(mediaType => MediaEditorSchemaCatalog.Resolve(mediaType).Groups)
            .SelectMany(group => group.Fields)
            .Select(field => field.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.DoesNotContain("cast_member", fields);
        Assert.DoesNotContain("director", fields);
        Assert.DoesNotContain("narrator", fields);
        Assert.DoesNotContain("composer", fields);
        Assert.DoesNotContain("lyrics", fields);
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
    [InlineData("Books", "title", "description")]
    [InlineData("Audiobooks", "title", "description")]
    [InlineData("Movies", "title", "tagline", "description")]
    [InlineData("TV", "title", "tagline", "description")]
    [InlineData("Music", "title", "description")]
    [InlineData("Comics", "title", "description")]
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
