using MediaEngine.Web.Services.Editing;
using MediaEngine.Web.Models.ViewDTOs;

namespace MediaEngine.Web.Tests;

public sealed class MediaEditorSchemaCatalogTests
{
    [Fact]
    public void BuildValueMap_ComicItemUsesIssueDescriptionInsteadOfInheritedSeriesDescription()
    {
        var detail = new LibraryItemDetailViewModel
        {
            MediaType = "Comics",
            Title = "Pilot",
            Description = "This is the description of the entire series.",
            CanonicalValues =
            [
                new LibraryItemCanonicalValueDto
                {
                    Key = "issue_description",
                    Value = "This synopsis belongs to the individual issue.",
                },
            ],
        };
        CanonicalFieldViewModel[] canonicals =
        [
            new("description", "This is the description of the entire series.", 0.9, "Wikipedia", false, false),
        ];

        var values = MediaEditorSchemaCatalog.BuildValueMap(detail, canonicals);

        Assert.Equal("This synopsis belongs to the individual issue.", values["description"]);
    }

    [Fact]
    public void BuildValueMap_ComicItemDoesNotFallBackToInheritedSeriesDescription()
    {
        var detail = new LibraryItemDetailViewModel
        {
            MediaType = "Comics",
            Title = "Pilot",
            Description = "This is the description of the entire series.",
        };
        CanonicalFieldViewModel[] canonicals =
        [
            new("description", "This is the description of the entire series.", 0.9, "Wikipedia", false, false),
        ];

        var values = MediaEditorSchemaCatalog.BuildValueMap(detail, canonicals);

        Assert.False(values.ContainsKey("description"));
    }

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
