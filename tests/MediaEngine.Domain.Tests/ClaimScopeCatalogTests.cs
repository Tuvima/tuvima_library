using MediaEngine.Domain;
using MediaEngine.Domain.Constants;
using MediaEngine.Domain.Enums;

namespace MediaEngine.Domain.Tests;

public class ClaimScopeCatalogTests
{
    // ── Music routing ────────────────────────────────────────────────────

    [Fact]
    public void Music_Album_RoutesToParent()
    {
        Assert.Equal(ClaimScope.Parent,
            ClaimScopeCatalog.GetScope(MetadataFieldConstants.Album, MediaType.Music));
    }

    [Fact]
    public void Music_Year_RoutesToParent()
    {
        // Year on a track means the album release year, which is shared.
        Assert.Equal(ClaimScope.Parent,
            ClaimScopeCatalog.GetScope(MetadataFieldConstants.Year, MediaType.Music));
    }

    [Fact]
    public void Music_TrackTitle_RoutesToSelf()
    {
        Assert.Equal(ClaimScope.Self,
            ClaimScopeCatalog.GetScope(MetadataFieldConstants.Title, MediaType.Music));
    }

    [Fact]
    public void Music_AppleMusicId_TrackLevel_RoutesToSelf()
    {
        Assert.Equal(ClaimScope.Self,
            ClaimScopeCatalog.GetScope(BridgeIdKeys.AppleMusicId, MediaType.Music));
    }

    [Fact]
    public void Music_AppleMusicCollectionId_AlbumLevel_RoutesToParent()
    {
        Assert.Equal(ClaimScope.Parent,
            ClaimScopeCatalog.GetScope(BridgeIdKeys.AppleMusicCollectionId, MediaType.Music));
    }

    // ── Movies (no parent) ───────────────────────────────────────────────

    [Fact]
    public void Music_WikidataQid_AlbumLevel_RoutesToParent()
    {
        Assert.Equal(ClaimScope.Parent,
            ClaimScopeCatalog.GetScope(BridgeIdKeys.WikidataQid, MediaType.Music));
    }

    [Fact]
    public void Movies_Year_RoutesToParent()
    {
        // Year on a movie is stored at the Work level (Parent) for storage
        // uniformity — all reader queries look up canonical values on works.id.
        // Movies are standalone so Parent collapses to the movie's own Work id.
        Assert.Equal(ClaimScope.Parent,
            ClaimScopeCatalog.GetScope(MetadataFieldConstants.Year, MediaType.Movies));
    }

    [Fact]
    public void Movies_TmdbId_StaysSelf()
    {
        Assert.Equal(ClaimScope.Self,
            ClaimScopeCatalog.GetScope(BridgeIdKeys.TmdbId, MediaType.Movies));
    }

    // ── TV routing (Show is the topmost parent) ─────────────────────────

    [Fact]
    public void TV_ShowName_RoutesToParent()
    {
        Assert.Equal(ClaimScope.Parent,
            ClaimScopeCatalog.GetScope(MetadataFieldConstants.ShowName, MediaType.TV));
    }

    [Fact]
    public void TV_Network_RoutesToParent()
    {
        Assert.Equal(ClaimScope.Parent,
            ClaimScopeCatalog.GetScope(MetadataFieldConstants.Network, MediaType.TV));
    }

    [Fact]
    public void TV_EpisodeNumber_RoutesToSelf()
    {
        Assert.Equal(ClaimScope.Self,
            ClaimScopeCatalog.GetScope(MetadataFieldConstants.EpisodeNumber, MediaType.TV));
    }

    [Fact]
    public void TV_Director_RoutesToSelf()
    {
        // Different episodes have different directors — TV-specific override.
        Assert.Equal(ClaimScope.Self,
            ClaimScopeCatalog.GetScope(MetadataFieldConstants.Director, MediaType.TV));
    }

    [Fact]
    public void TV_CastMember_RoutesToParent()
    {
        Assert.Equal(ClaimScope.Parent,
            ClaimScopeCatalog.GetScope(MetadataFieldConstants.CastMember, MediaType.TV));
    }

    // ── Books / Audiobooks ───────────────────────────────────────────────

    [Fact]
    public void Books_Author_RoutesToParent()
    {
        // Series-bound books inherit author from the series.
        Assert.Equal(ClaimScope.Parent,
            ClaimScopeCatalog.GetScope(MetadataFieldConstants.Author, MediaType.Books));
    }

    [Fact]
    public void Books_Title_RoutesToSelf()
    {
        Assert.Equal(ClaimScope.Self,
            ClaimScopeCatalog.GetScope(MetadataFieldConstants.Title, MediaType.Books));
    }

    [Fact]
    public void Audiobooks_Narrator_RoutesToParent()
    {
        Assert.Equal(ClaimScope.Parent,
            ClaimScopeCatalog.GetScope(MetadataFieldConstants.Narrator, MediaType.Audiobooks));
    }

    [Theory]
    [InlineData(MediaType.Books)]
    [InlineData(MediaType.Audiobooks)]
    [InlineData(MediaType.Comics)]
    [InlineData(MediaType.TV)]
    [InlineData(MediaType.Movies)]
    public void NarrativeUniverseClaims_RouteToParent(MediaType mediaType)
    {
        Assert.Equal(ClaimScope.Parent,
            ClaimScopeCatalog.GetScope(MetadataFieldConstants.FictionalUniverse, mediaType));
        Assert.Equal(ClaimScope.Parent,
            ClaimScopeCatalog.GetScope("fictional_universe_qid", mediaType));
        Assert.Equal(ClaimScope.Parent,
            ClaimScopeCatalog.GetScope(MetadataFieldConstants.Characters, mediaType));
        Assert.Equal(ClaimScope.Parent,
            ClaimScopeCatalog.GetScope("characters_qid", mediaType));
        Assert.Equal(ClaimScope.Parent,
            ClaimScopeCatalog.GetScope(MetadataFieldConstants.NarrativeLocation, mediaType));
        Assert.Equal(ClaimScope.Parent,
            ClaimScopeCatalog.GetScope("narrative_location_qid", mediaType));
    }

    // ── Comics ───────────────────────────────────────────────────────────

    [Fact]
    public void Comics_Series_RoutesToParent()
    {
        Assert.Equal(ClaimScope.Parent,
            ClaimScopeCatalog.GetScope(MetadataFieldConstants.Series, MediaType.Comics));
    }

    [Fact]
    public void Comics_IssueNumberEquivalent_TitleRoutesToSelf()
    {
        Assert.Equal(ClaimScope.Self,
            ClaimScopeCatalog.GetScope(MetadataFieldConstants.Title, MediaType.Comics));
    }

    // ── Companion QID suffix handling ────────────────────────────────────

    [Fact]
    public void CompanionQid_InheritsScope_FromPrimaryKey()
    {
        // genre → Parent for music, so genre_qid must also be Parent.
        Assert.Equal(ClaimScope.Parent,
            ClaimScopeCatalog.GetScope("genre_qid", MediaType.Music));
    }

    [Fact]
    public void CompanionQid_DirectorOnTV_InheritsSelf()
    {
        Assert.Equal(ClaimScope.Self,
            ClaimScopeCatalog.GetScope("director_qid", MediaType.TV));
    }

    // ── Default fallback ─────────────────────────────────────────────────

    [Fact]
    public void UnknownKey_DefaultsToSelf()
    {
        Assert.Equal(ClaimScope.Self,
            ClaimScopeCatalog.GetScope("some_brand_new_field", MediaType.Music));
    }

    [Fact]
    public void EmptyKey_DefaultsToSelf()
    {
        Assert.Equal(ClaimScope.Self,
            ClaimScopeCatalog.GetScope("", MediaType.Music));
    }
}
