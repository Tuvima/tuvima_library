using MediaEngine.Domain;
using MediaEngine.Providers.Helpers;

namespace MediaEngine.Providers.Tests;

/// <summary>
/// Tests for <see cref="BridgeIdHelper"/> static methods:
/// IsBridgeId recognition and InjectSentinels sentinel injection.
/// </summary>
public sealed class BridgeIdHelperTests
{
    // ── IsBridgeId ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData(BridgeIdKeys.Isbn, true)]
    [InlineData(BridgeIdKeys.Isbn13, true)]
    [InlineData(BridgeIdKeys.Isbn10, true)]
    [InlineData(BridgeIdKeys.Asin, true)]
    [InlineData(BridgeIdKeys.AppleBooksId, true)]
    [InlineData(BridgeIdKeys.TmdbId, true)]
    [InlineData(BridgeIdKeys.ImdbId, true)]
    [InlineData(BridgeIdKeys.AudibleId, true)]
    [InlineData(BridgeIdKeys.GoodreadsId, true)]
    [InlineData(BridgeIdKeys.MusicBrainzId, true)]
    [InlineData(BridgeIdKeys.ComicVineId, true)]
    [InlineData("gcd_id", true)]
    [InlineData("apple_music_id", true)]
    public void IsBridgeId_KnownKeys_ReturnsTrue(string key, bool expected)
    {
        Assert.Equal(expected, BridgeIdHelper.IsBridgeId(key));
    }

    [Theory]
    [InlineData("title")]
    [InlineData("author")]
    [InlineData("year")]
    [InlineData("description")]
    [InlineData("cover_url")]
    [InlineData("rating")]
    [InlineData("genre")]
    [InlineData("director")]
    [InlineData("")]
    [InlineData("random_field")]
    public void IsBridgeId_NonBridgeKeys_ReturnsFalse(string key)
    {
        Assert.False(BridgeIdHelper.IsBridgeId(key));
    }

    // ── InjectSentinels ─────────────────────────────────────────────────────

    [Fact]
    public void InjectSentinels_AddsTitleAndAuthor()
    {
        var dict = new Dictionary<string, string>();
        BridgeIdHelper.InjectSentinels(dict, "Dune", "Frank Herbert");

        Assert.Equal("Dune", dict["_title"]);
        Assert.Equal("Frank Herbert", dict["_author"]);
    }

    [Fact]
    public void InjectSentinels_SkipsNullTitle()
    {
        var dict = new Dictionary<string, string>();
        BridgeIdHelper.InjectSentinels(dict, null, "Frank Herbert");

        Assert.False(dict.ContainsKey("_title"));
        Assert.Equal("Frank Herbert", dict["_author"]);
    }

    [Fact]
    public void InjectSentinels_SkipsWhitespaceAuthor()
    {
        var dict = new Dictionary<string, string>();
        BridgeIdHelper.InjectSentinels(dict, "Dune", "   ");

        Assert.Equal("Dune", dict["_title"]);
        Assert.False(dict.ContainsKey("_author"));
    }

    [Fact]
    public void InjectSentinels_BothNull_DictEmpty()
    {
        var dict = new Dictionary<string, string>();
        BridgeIdHelper.InjectSentinels(dict, null, null);

        Assert.Empty(dict);
    }

    [Fact]
    public void InjectSentinels_DoesNotOverwriteExisting()
    {
        var dict = new Dictionary<string, string>
        {
            [BridgeIdKeys.Isbn13] = "978-0441172719"
        };
        BridgeIdHelper.InjectSentinels(dict, "Dune", "Frank Herbert");

        Assert.Equal(3, dict.Count);
        Assert.Equal("978-0441172719", dict[BridgeIdKeys.Isbn13]);
    }
}
