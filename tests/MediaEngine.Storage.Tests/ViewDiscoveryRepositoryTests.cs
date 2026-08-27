using Dapper;
using MediaEngine.Domain.PersonalMedia;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Storage.Tests;

public sealed class ViewDiscoveryRepositoryTests : IDisposable
{
    private sealed class QueryPlanRow
    {
        public string Detail { get; init; } = string.Empty;
    }

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"tuvima-view-discovery-{Guid.NewGuid():N}.db");
    private readonly DatabaseConnection _database;
    private readonly LocalAssetRepository _assets;
    private readonly ViewPersonalSpaceRepository _spaces;
    private readonly ViewDiscoveryRepository _discovery;
    private char _hash = '1';

    public ViewDiscoveryRepositoryTests()
    {
        _database = new DatabaseConnection(_path);
        _database.InitializeSchema();
        _assets = new LocalAssetRepository(_database);
        _spaces = new ViewPersonalSpaceRepository(_database);
        _discovery = new ViewDiscoveryRepository(_database);
    }

    [Fact]
    public async Task Places_UsesOnlyAuthorizedActiveVisibleAssetsAndSupportsCursorAndSearch()
    {
        var first = await CreateOwnership();
        var second = await CreateOwnership();
        var privateOwner = await CreateOwnership();
        var chicagoOne = await AddAsset(first, "Chicago one", 41.8781, -87.6298, "Chicago");
        await AddAsset(first, "Chicago two", 41.8790, -87.6301, "Chicago");
        await AddAsset(second, "Chicago three", 41.8800, -87.6310, "Chicago");
        await AddAsset(first, "Maui", 20.7984, -156.3319, "Maui");
        var hidden = await AddAsset(first, "Hidden Paris", 48.8566, 2.3522, "Paris");
        await _assets.SetFlagsAsync(hidden, favorite: null, hidden: true);
        var archived = await AddAsset(first, "Archived Rome", 41.9028, 12.4964, "Rome");
        await _assets.SetLifecycleStateAsync(archived, LocalAssetLifecycleState.Archived);
        await AddAsset(privateOwner, "Private Tokyo", 35.6762, 139.6503, "Tokyo");

        var firstPage = _discovery.QueryPlaces(new ViewPlaceDiscoveryQuery(
            [first.LibraryId, second.LibraryId], Limit: 1));

        var chicago = Assert.Single(firstPage.Items);
        Assert.StartsWith("chicago@", chicago.Key, StringComparison.Ordinal);
        Assert.Equal(3, chicago.AssetCount);
        Assert.True(firstPage.HasMore);
        Assert.NotNull(firstPage.NextCursor);
        Assert.NotEqual(Guid.Empty, chicago.RepresentativeAssetId);
        Assert.NotEqual(chicagoOne, Guid.Empty);

        var secondPage = _discovery.QueryPlaces(new ViewPlaceDiscoveryQuery(
            [first.LibraryId, second.LibraryId], Limit: 10, Cursor: firstPage.NextCursor));
        Assert.StartsWith("maui@", Assert.Single(secondPage.Items).Key, StringComparison.Ordinal);
        Assert.DoesNotContain(secondPage.Items, place => place.Name is "Paris" or "Rome" or "Tokyo");

        var search = _discovery.QueryPlaces(new ViewPlaceDiscoveryQuery(
            [first.LibraryId], Search: "Mau"));
        Assert.Equal("Maui", Assert.Single(search.Items).Name);
        Assert.True(search.HasEligibleData);
    }

    [Fact]
    public async Task People_RequiresNamedOrReviewedIdentityEvidenceAndRetainsProvenance()
    {
        var owner = await CreateOwnership();
        var privateOwner = await CreateOwnership();
        var alice = await AddAsset(owner, "Alice photo", null, null, null);
        var bob = await AddAsset(owner, "Bob photo", null, null, null);
        var unreviewed = await AddAsset(owner, "Unreviewed face", null, null, null);
        var ignored = await AddAsset(owner, "Object label", null, null, null);
        var privateAsset = await AddAsset(privateOwner, "Private person", null, null, null);
        await _assets.AddAnnotationAsync(alice, new LocalAssetAnnotation(
            "person_name", "Alice", "user", ProvenanceJson: "{\"method\":\"manual\"}"));
        await _assets.AddAnnotationAsync(bob, new LocalAssetAnnotation(
            "face_identity", "Bob", "review-tool", Confidence: 0.98, ReviewedAt: DateTimeOffset.UtcNow));
        await _assets.AddAnnotationAsync(unreviewed, new LocalAssetAnnotation(
            "face_identity", "Unreviewed", "future-face-worker", Confidence: 0.60));
        await _assets.AddAnnotationAsync(ignored, new LocalAssetAnnotation(
            "object_label", "Person-shaped object", "metadata"));
        await _assets.AddAnnotationAsync(privateAsset, new LocalAssetAnnotation(
            "person_name", "Private Eve", "user"));

        var page = _discovery.QueryPeople(new ViewPeopleDiscoveryQuery([owner.LibraryId]));

        Assert.Equal(2, page.Items.Count);
        var aliceResult = Assert.Single(page.Items, person => person.DisplayName == "Alice");
        Assert.Contains("person_name", aliceResult.AnnotationKinds);
        Assert.Contains("user", aliceResult.ProvenanceSources);
        Assert.False(aliceResult.HasReviewedEvidence);
        var bobResult = Assert.Single(page.Items, person => person.DisplayName == "Bob");
        Assert.True(bobResult.HasReviewedEvidence);
        Assert.Contains("face_identity", bobResult.AnnotationKinds);
        Assert.DoesNotContain(page.Items, person => person.DisplayName is "Unreviewed" or "Private Eve");
    }

    [Fact]
    public void Schema_HasDiscoveryScopeAndEvidenceIndexes()
    {
        using var connection = _database.CreateConnection();
        var indexes = connection.Query<string>("""
            SELECT name FROM sqlite_master
             WHERE type = 'index' AND name LIKE 'ix_%discovery'
                OR type = 'index' AND name = 'ix_local_item_annotations_people';
            """).ToHashSet(StringComparer.Ordinal);
        Assert.Contains("ix_local_items_active_discovery", indexes);
        Assert.Contains("ix_local_item_annotations_people", indexes);

        var plan = connection.Query<QueryPlanRow>("""
            EXPLAIN QUERY PLAN
            SELECT li.id
              FROM local_items li
              JOIN local_item_metadata lm ON lm.item_id = li.id
             WHERE li.library_id = zeroblob(16)
               AND li.hidden = 0 AND li.archived_at IS NULL AND li.trashed_at IS NULL
               AND lm.latitude IS NOT NULL AND lm.longitude IS NOT NULL;
            """).ToList();
        Assert.Contains(plan, step => step.Detail.Contains("ix_local_items_active_discovery", StringComparison.Ordinal));
    }

    private async Task<(Guid ProfileId, Guid SpaceId, Guid LibraryId)> CreateOwnership()
    {
        var profileId = Guid.NewGuid();
        var libraryId = Guid.NewGuid();
        using (var connection = _database.CreateConnection())
        {
            connection.Execute("""
                INSERT INTO profiles (id, display_name, avatar_color, role, created_at)
                VALUES (@profileId, @name, '#7C4DFF', 'RestrictedProfile', @now);
                """, new { profileId, name = $"Profile {profileId:N}", now = DateTimeOffset.UtcNow });
        }
        var space = await _spaces.CreateAsync(profileId, libraryId);
        return (profileId, space.Id, libraryId);
    }

    private async Task<Guid> AddAsset(
        (Guid ProfileId, Guid SpaceId, Guid LibraryId) owner,
        string title,
        double? latitude,
        double? longitude,
        string? locationName)
    {
        var hashCharacter = _hash++;
        var result = await _assets.UpsertAsync(new LocalAssetRegistration(
            owner.LibraryId,
            owner.SpaceId,
            owner.ProfileId,
            LocalAssetMediaKinds.Image,
            title,
            DateTimeOffset.UtcNow.AddMinutes(-_hash),
            [new LocalAssetFileRegistration(
                $@"C:\personal\{title}.jpg",
                new string(hashCharacter, 64),
                $"{title}.jpg",
                "image/jpeg",
                1024,
                DateTimeOffset.UtcNow)],
            Latitude: latitude,
            Longitude: longitude,
            LocationName: locationName));
        return result.ItemId;
    }

    public void Dispose()
    {
        _database.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_path)) File.Delete(_path);
    }
}
