using MediaEngine.Domain;
using MediaEngine.Domain.Constants;
using MediaEngine.Domain.Enums;

namespace MediaEngine.Domain.Tests;

public class ClaimScopeRegistryTests
{
    // ── Music routing ────────────────────────────────────────────────────

    [Fact]
    public void Music_Album_RoutesToParent()
    {
        Assert.Equal(ClaimScope.Parent,
            ClaimScopeRegistry.GetScope(MetadataFieldConstants.Album, MediaType.Music));
    }

    [Fact]
    public void Music_Year_RoutesToParent()
    {
        // Year on a track means the album release year, which is shared.
        Assert.Equal(ClaimScope.Parent,
            ClaimScopeRegistry.GetScope(MetadataFieldConstants.Year, MediaType.Music));
    }

    [Fact]
    public void Music_TrackTitle_RoutesToSelf()
    {
        Assert.Equal(ClaimScope.Self,
            ClaimScopeRegistry.GetScope(MetadataFieldConstants.Title, MediaType.Music));
    }

    [Fact]
    public void Music_AppleMusicId_TrackLevel_RoutesToSelf()
    {
        Assert.Equal(ClaimScope.Self,
            ClaimScopeRegistry.GetScope(BridgeIdKeys.AppleMusicId, MediaType.Music));
    }

    [Fact]
    public void Music_AppleMusicCollectionId_AlbumLevel_RoutesToParent()
    {
        Assert.Equal(ClaimScope.Parent,
            ClaimScopeRegistry.GetScope(BridgeIdKeys.AppleMusicCollectionId, MediaType.Music));
    }

    // ── Movies (no parent) ───────────────────────────────────────────────

    [Fact]
    public void Movies_Year_RoutesToParent()
    {
        // Year on a movie is stored at the Work level (Parent) for storage
        // uniformity — all reader queries look up canonical values on works.id.
        // Movies are standalone so Parent collapses to the movie's own Work id.
        Assert.Equal(ClaimScope.Parent,
            ClaimScopeRegistry.GetScope(MetadataFieldConstants.Year, MediaType.Movies));
    }

    [Fact]
    public void Movies_TmdbId_StaysSelf()
    {
        Assert.Equal(ClaimScope.Self,
            ClaimScopeRegistry.GetScope(BridgeIdKeys.TmdbId, MediaType.Movies));
    }

    // ── TV routing (Show is the topmost parent) ─────────────────────────

    [Fact]
    public void TV_ShowName_RoutesToParent()
    {
        Assert.Equal(ClaimScope.Parent,
            ClaimScopeRegistry.GetScope(MetadataFieldConstants.ShowName, MediaType.TV));
    }

    [Fact]
    public void TV_Network_RoutesToParent()
    {
        Assert.Equal(ClaimScope.Parent,
            ClaimScopeRegistry.GetScope(MetadataFieldConstants.Network, MediaType.TV));
    }

    [Fact]
    public void TV_EpisodeNumber_RoutesToSelf()
    {
        Assert.Equal(ClaimScope.Self,
            ClaimScopeRegistry.GetScope(MetadataFieldConstants.EpisodeNumber, MediaType.TV));
    }

    [Fact]
    public void TV_Director_RoutesToSelf()
    {
        // Different episodes have different directors — TV-specific override.
        Assert.Equal(ClaimScope.Self,
            ClaimScopeRegistry.GetScope(MetadataFieldConstants.Director, MediaType.TV));
    }

    [Fact]
    public void TV_CastMember_RoutesToParent()
    {
        Assert.Equal(ClaimScope.Parent,
            ClaimScopeRegistry.GetScope(MetadataFieldConstants.CastMember, MediaType.TV));
    }

    // ── Books / Audiobooks ───────────────────────────────────────────────

    [Fact]
    public void Books_Author_RoutesToParent()
    {
        // Series-bound books inherit author from the series.
        Assert.Equal(ClaimScope.Parent,
            ClaimScopeRegistry.GetScope(MetadataFieldConstants.Author, MediaType.Books));
    }

    [Fact]
    public void Books_Title_RoutesToSelf()
    {
        Assert.Equal(ClaimScope.Self,
            ClaimScopeRegistry.GetScope(MetadataFieldConstants.Title, MediaType.Books));
    }

    [Fact]
    public void Audiobooks_Narrator_RoutesToParent()
    {
        Assert.Equal(ClaimScope.Parent,
            ClaimScopeRegistry.GetScope(MetadataFieldConstants.Narrator, MediaType.Audiobooks));
    }

    // ── Comics ───────────────────────────────────────────────────────────

    [Fact]
    public void Comics_Series_RoutesToParent()
    {
        Assert.Equal(ClaimScope.Parent,
            ClaimScopeRegistry.GetScope(MetadataFieldConstants.Series, MediaType.Comics));
    }

    [Fact]
    public void Comics_IssueNumberEquivalent_TitleRoutesToSelf()
    {
        Assert.Equal(ClaimScope.Self,
            ClaimScopeRegistry.GetScope(MetadataFieldConstants.Title, MediaType.Comics));
    }

    // ── Companion QID suffix handling ────────────────────────────────────

    [Fact]
    public void CompanionQid_InheritsScope_FromPrimaryKey()
    {
        // genre → Parent for music, so genre_qid must also be Parent.
        Assert.Equal(ClaimScope.Parent,
            ClaimScopeRegistry.GetScope("genre_qid", MediaType.Music));
    }

    [Fact]
    public void CompanionQid_DirectorOnTV_InheritsSelf()
    {
        Assert.Equal(ClaimScope.Self,
            ClaimScopeRegistry.GetScope("director_qid", MediaType.TV));
    }

    // ── Default fallback ─────────────────────────────────────────────────

    [Fact]
    public void UnknownKey_DefaultsToSelf()
    {
        Assert.Equal(ClaimScope.Self,
            ClaimScopeRegistry.GetScope("some_brand_new_field", MediaType.Music));
    }

    [Fact]
    public void EmptyKey_DefaultsToSelf()
    {
        Assert.Equal(ClaimScope.Self,
            ClaimScopeRegistry.GetScope("", MediaType.Music));
    }
}
