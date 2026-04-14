using MediaEngine.Web.Models.ViewDTOs;
using MediaEngine.Web.Services.Editing;

namespace MediaEngine.Web.Tests;

public sealed class MediaEditorSchemaCatalogTests
{
    [Fact]
    public void Resolve_MusicSchema_ExposesAlbumArtistAndTrackTargets()
    {
        var schema = MediaEditorSchemaCatalog.Resolve("Music");

        Assert.Equal("Music", schema.MediaType);
        Assert.Equal("album", schema.DefaultTargetGroup);
        Assert.Contains(schema.QuickSearchTargets, target => target.Key == "album");
        Assert.Contains(schema.QuickSearchTargets, target => target.Key == "artist");
        Assert.Contains(schema.QuickSearchTargets, target => target.Key == "track");
    }

    [Fact]
    public void ResolveBatchFields_MixedMedia_ReturnsSharedSafeFieldsOnly()
    {
        var fields = MediaEditorSchemaCatalog.ResolveBatchFields(["Music", "Movies"]);
        var keys = fields.Select(field => field.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Equal(5, keys.Count);
        Assert.Contains("title", keys);
        Assert.Contains("year", keys);
        Assert.Contains("genre", keys);
        Assert.Contains("language", keys);
        Assert.Contains("rating", keys);
    }

    [Fact]
    public void BuildValueMap_UsesAvailableCanonicalsWithoutThrowingOnMissingFields()
    {
        var detail = new RegistryItemDetailViewModel
        {
            EntityId = Guid.NewGuid(),
            Title = "Closer",
            MediaType = "Music",
            Year = "1994",
        };

        var canonicals = new[]
        {
            new CanonicalFieldViewModel("artist", "Nine Inch Nails", 1.0, null, false, false),
        };

        var values = MediaEditorSchemaCatalog.BuildValueMap(detail, canonicals);

        Assert.Equal("Closer", values["title"]);
        Assert.Equal("1994", values["year"]);
        Assert.Equal("Nine Inch Nails", values["artist"]);
        Assert.False(values.ContainsKey("album"));
    }
}
