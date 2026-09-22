using Dapper;
using MediaEngine.Api.Services;
using MediaEngine.Contracts.Artwork;
using MediaEngine.Domain.Services;
using MediaEngine.Storage;

namespace MediaEngine.Api.Tests;

public sealed class ArtworkAssetServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"tuvima_artwork_{Guid.NewGuid():N}");
    private readonly DatabaseConnection _database;
    private readonly ArtworkAssetService _service;

    public ArtworkAssetServiceTests()
    {
        Directory.CreateDirectory(_root);
        _database = new DatabaseConnection(Path.Combine(_root, "library.db"));
        _database.InitializeSchema();
        _database.RunStartupChecks();
        _service = new ArtworkAssetService(_database, new AssetPathService(_root), new NoOpHttpClientFactory());
    }

    [Fact]
    public async Task BrowseAsync_RecommendedRanksTargetContextWhileRelatedRemainsScoped()
    {
        var targetId = Guid.NewGuid();
        var relatedAssetId = Guid.NewGuid();
        var globalAssetId = Guid.NewGuid();
        SeedAsset(relatedAssetId, "related", "Spirited Away Chihiro poster", targetId);
        SeedAsset(globalAssetId, "global", "Another library poster", Guid.NewGuid());

        var recommended = await _service.BrowseAsync(new ArtworkAssetQuery(
            TargetEntityId: targetId,
            TargetEntityType: "Work",
            TargetRole: "Primary",
            PickerScope: ArtworkPickerScope.Recommended,
            Sort: ArtworkAssetSort.Relevance), CancellationToken.None);
        var related = await _service.BrowseAsync(new ArtworkAssetQuery(
            TargetEntityId: targetId,
            TargetEntityType: "Work",
            TargetRole: "Primary",
            PickerScope: ArtworkPickerScope.Related), CancellationToken.None);

        Assert.Equal(2, recommended.Total);
        Assert.Equal(relatedAssetId, recommended.Items[0].Id);
        Assert.Equal("Already linked to Spirited Away", recommended.Items[0].MatchExplanation);
        Assert.Equal(relatedAssetId, Assert.Single(related.Items).Id);
    }

    [Fact]
    public async Task BrowseAndWorkspace_PreserveFtsIdentityAndSourceSlot()
    {
        var targetId = Guid.NewGuid();
        var assetId = Guid.NewGuid();
        SeedAsset(assetId, "episode", "The Expanse Rocinante episode still Q12345", Guid.NewGuid());

        await _service.LinkAsync("Work", targetId,
            new ArtworkLinkRequest(assetId, "Primary", "Episode", true,
                "The Expanse S1 E1", "TV", "2015", "EpisodeStill"), CancellationToken.None);

        var result = await _service.BrowseAsync(new ArtworkAssetQuery(Search: "Rocinante"), CancellationToken.None);
        var workspace = await _service.GetEntityAsync(
            "Work", targetId, "TV", "Episode", ["EpisodeStill"], CancellationToken.None);

        Assert.Contains(result.Items, item => item.Id == assetId);
        var variant = Assert.Single(workspace.Variants);
        Assert.Equal("EpisodeStill", variant.SourceAssetType);
        var role = Assert.Single(workspace.SupportedRoles);
        Assert.Equal("Primary", role.Role);
        Assert.Equal("EpisodeStill", role.SourceAssetType);
        Assert.Equal("still", role.PresentationKey);
    }

    [Fact]
    public async Task BrowseAsync_AppliesLibraryMediaEntityAndUsageFilters()
    {
        var movieAssetId = Guid.NewGuid();
        var personAssetId = Guid.NewGuid();
        var unusedAssetId = Guid.NewGuid();
        SeedAsset(movieAssetId, "movie", "Example movie poster", Guid.NewGuid(), mediaType: "Movies", preferred: true);
        SeedAsset(personAssetId, "person", "Example actor portrait", Guid.NewGuid(), entityType: "Person", mediaType: null);
        SeedAsset(unusedAssetId, "unused", "Unused book cover", Guid.NewGuid(), mediaType: "Books", linked: false);

        var movies = await _service.BrowseAsync(new ArtworkAssetQuery(MediaTypes: ["Movie"]), CancellationToken.None);
        var people = await _service.BrowseAsync(new ArtworkAssetQuery(EntityTypes: ["Person"]), CancellationToken.None);
        var preferred = await _service.BrowseAsync(new ArtworkAssetQuery(Usage: ArtworkUsageFilter.Selected), CancellationToken.None);
        var unused = await _service.BrowseAsync(new ArtworkAssetQuery(Usage: ArtworkUsageFilter.Unlinked), CancellationToken.None);

        Assert.Equal(movieAssetId, Assert.Single(movies.Items).Id);
        Assert.Equal(personAssetId, Assert.Single(people.Items).Id);
        Assert.Equal(movieAssetId, Assert.Single(preferred.Items).Id);
        Assert.Equal(unusedAssetId, Assert.Single(unused.Items).Id);
    }

    private void SeedAsset(
        Guid assetId,
        string hashSuffix,
        string searchText,
        Guid contextEntityId,
        string entityType = "Work",
        string? mediaType = "Movies",
        bool linked = true,
        bool preferred = false)
    {
        using var connection = _database.CreateConnection();
        connection.Execute(
            """
            INSERT INTO artwork_assets
                (id, content_hash, original_path, width_px, height_px, aspect_class, source_provider, created_at)
            VALUES (@assetId, @hash, @path, 1000, 1500, 'Portrait', 'test', @createdAt);
            INSERT INTO artwork_asset_context
                (artwork_asset_id, entity_id, entity_type, entity_label, media_type, year, role, provider, canonical_id, search_text, created_at)
            VALUES (@assetId, @contextEntityId, @entityType, @label, @mediaType, '2001', 'Primary', 'test', @canonicalId, @searchText, @createdAt);
            """,
            new
            {
                assetId,
                hash = $"hash-{hashSuffix}-{assetId:N}",
                path = Path.Combine(_root, $"{assetId:N}.jpg"),
                createdAt = DateTimeOffset.UtcNow.ToString("O"),
                contextEntityId,
                entityType,
                mediaType,
                label = searchText.StartsWith("Spirited", StringComparison.Ordinal) ? "Spirited Away" : "Library item",
                canonicalId = $"Q{Math.Abs(assetId.GetHashCode())}",
                searchText,
            });
        if (linked)
        {
            connection.Execute(
                """
                INSERT INTO entity_artwork_links
                    (id, entity_id, entity_type, artwork_asset_id, role, context, source_asset_type, is_preferred, created_at)
                VALUES (@linkId, @contextEntityId, @entityType, @assetId, 'Primary', '', 'CoverArt', @preferred, @createdAt);
                """,
                new
                {
                    linkId = Guid.NewGuid(),
                    contextEntityId,
                    entityType,
                    assetId,
                    preferred,
                    createdAt = DateTimeOffset.UtcNow.ToString("O"),
                });
        }
    }

    public void Dispose()
    {
        _database.Dispose();
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private sealed class NoOpHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
