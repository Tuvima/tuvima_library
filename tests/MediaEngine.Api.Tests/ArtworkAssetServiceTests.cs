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

    private void SeedAsset(Guid assetId, string hashSuffix, string searchText, Guid contextEntityId)
    {
        using var connection = _database.CreateConnection();
        connection.Execute(
            """
            INSERT INTO artwork_assets
                (id, content_hash, original_path, width_px, height_px, aspect_class, source_provider, created_at)
            VALUES (@assetId, @hash, @path, 1000, 1500, 'Portrait', 'test', @createdAt);
            INSERT INTO entity_artwork_links
                (id, entity_id, entity_type, artwork_asset_id, role, context, source_asset_type, is_preferred, created_at)
            VALUES (@linkId, @contextEntityId, 'Work', @assetId, 'Primary', '', 'CoverArt', 0, @createdAt);
            INSERT INTO artwork_asset_context
                (artwork_asset_id, entity_id, entity_type, entity_label, media_type, year, role, provider, canonical_id, search_text, created_at)
            VALUES (@assetId, @contextEntityId, 'Work', @label, 'Movies', '2001', 'Primary', 'test', @canonicalId, @searchText, @createdAt);
            """,
            new
            {
                assetId,
                hash = $"hash-{hashSuffix}-{assetId:N}",
                path = Path.Combine(_root, $"{assetId:N}.jpg"),
                createdAt = DateTimeOffset.UtcNow.ToString("O"),
                linkId = Guid.NewGuid(),
                contextEntityId,
                label = searchText.StartsWith("Spirited", StringComparison.Ordinal) ? "Spirited Away" : "Library item",
                canonicalId = $"Q{Math.Abs(assetId.GetHashCode())}",
                searchText,
            });
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
