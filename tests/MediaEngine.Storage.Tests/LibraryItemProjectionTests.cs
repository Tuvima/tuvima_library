using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Models;

namespace MediaEngine.Storage.Tests;

public sealed class LibraryItemProjectionTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DatabaseConnection _db;

    public LibraryItemProjectionTests()
    {
        DapperConfiguration.Configure();
        _dbPath = Path.Combine(Path.GetTempPath(), $"tuvima_libraryItem_projection_{Guid.NewGuid():N}.db");
        _db = new DatabaseConnection(_dbPath);
        _db.InitializeSchema();
        _db.RunStartupChecks();
    }

    public void Dispose()
    {
        try { _db.Dispose(); } catch { }
        try { File.Delete(_dbPath); } catch { }
    }

    [Fact]
    public async Task LibraryItemProjection_AppliesVaultGateAndNormalizedOverviewCounts()
    {
        var now = DateTimeOffset.UtcNow;

        var (visibleWorkId, visibleAssetId) = await BuildStandaloneWorkAsync("Books");
        await InsertCanonicalAsync(visibleAssetId, "title", "Dune");
        await InsertCanonicalAsync(visibleWorkId, "wikidata_qid", "Q123");
        await InsertCanonicalAsync(visibleWorkId, "cover_state", "present");
        await InsertCanonicalAsync(visibleWorkId, "cover_source", "provider");
        await InsertCanonicalAsync(visibleWorkId, "artwork_settled_at", now.ToString("O"));

        var (hiddenWorkId, hiddenAssetId) = await BuildStandaloneWorkAsync("Movies");
        await InsertCanonicalAsync(hiddenAssetId, "title", "Hidden Pending Art");
        await InsertCanonicalAsync(hiddenWorkId, "cover_state", "pending");

        var (reviewWorkId, reviewAssetId) = await BuildStandaloneWorkAsync("Audiobooks");
        await InsertCanonicalAsync(reviewAssetId, "title", "Needs Review");
        await InsertCanonicalAsync(reviewWorkId, "cover_state", "present");
        await InsertCanonicalAsync(reviewWorkId, "artwork_settled_at", now.ToString("O"));
        await CreateIdentityJobAsync(reviewAssetId, IdentityJobState.RetailMatchedNeedsReview);

        var (qidNoMatchWorkId, qidNoMatchAssetId) = await BuildStandaloneWorkAsync("Music");
        await InsertCanonicalAsync(qidNoMatchAssetId, "title", "No Qid Yet");
        await InsertCanonicalAsync(qidNoMatchWorkId, "cover_state", "missing");
        await InsertCanonicalAsync(qidNoMatchWorkId, "cover_source", "none");
        await InsertCanonicalAsync(qidNoMatchWorkId, "artwork_settled_at", now.ToString("O"));
        await CreateIdentityJobAsync(qidNoMatchAssetId, IdentityJobState.QidNoMatch);

        var repo = new LibraryItemRepository(_db);

        var page = await repo.GetPageAsync(new LibraryItemQuery());
        Assert.Equal(2, page.TotalCount);
        Assert.Equal(2, page.Items.Count);
        Assert.Contains(page.Items, i => i.EntityId == visibleWorkId);
        Assert.Contains(page.Items, i => i.EntityId == qidNoMatchWorkId);
        Assert.DoesNotContain(page.Items, i => i.EntityId == hiddenWorkId);
        Assert.DoesNotContain(page.Items, i => i.EntityId == reviewWorkId);

        var visibleListItem = page.Items.Single(i => i.EntityId == visibleWorkId);
        var visibleDetail = await repo.GetDetailAsync(visibleWorkId);
        Assert.NotNull(visibleDetail);
        Assert.Equal("visible", visibleListItem.LibraryVisibility);
        Assert.True(visibleListItem.IsReadyForLibrary);
        Assert.Equal("present", visibleListItem.ArtworkState);
        Assert.Equal(visibleListItem.PipelineStep, visibleDetail!.PipelineStep);
        Assert.Equal(visibleListItem.LibraryVisibility, visibleDetail.LibraryVisibility);
        Assert.Equal(visibleListItem.ArtworkState, visibleDetail.ArtworkState);
        Assert.Equal(visibleListItem.CoverUrl, visibleDetail.CoverUrl);

        var qidNoMatchDetail = await repo.GetDetailAsync(qidNoMatchWorkId);
        Assert.NotNull(qidNoMatchDetail);
        Assert.Equal("visible", qidNoMatchDetail!.LibraryVisibility);
        Assert.True(qidNoMatchDetail.IsReadyForLibrary);
        Assert.Equal("missing", qidNoMatchDetail.ArtworkState);
        Assert.Equal("Wikidata", qidNoMatchDetail.PipelineStep);
        Assert.Equal("QidNoMatch", qidNoMatchDetail.Status);
        Assert.Null(qidNoMatchDetail.CoverUrl);

        var hiddenDetail = await repo.GetDetailAsync(hiddenWorkId);
        Assert.NotNull(hiddenDetail);
        Assert.Equal("hidden", hiddenDetail!.LibraryVisibility);
        Assert.False(hiddenDetail.IsReadyForLibrary);
        Assert.Equal("pending", hiddenDetail.ArtworkState);

        var reviewDetail = await repo.GetDetailAsync(reviewWorkId);
        Assert.NotNull(reviewDetail);
        Assert.Equal("review_only", reviewDetail!.LibraryVisibility);
        Assert.Equal("Retail", reviewDetail.PipelineStep);

        var summary = await repo.GetProjectionSummaryAsync(CancellationToken.None);
        Assert.Equal(4, summary.TotalItems);
        Assert.Equal(1, summary.HiddenByQualityGate);
        Assert.Equal(1, summary.ArtPending);
        Assert.Equal(1, summary.RetailNeedsReview);
        Assert.Equal(1, summary.QidNoMatch);
        Assert.Equal(1, summary.CompletedWithArt);
    }

    [Fact]
    public async Task LibraryItemProjection_RequiresArtworkSettlementBeforeMissingCoverIsVisible()
    {
        var (workId, assetId) = await BuildStandaloneWorkAsync("Books");
        await InsertCanonicalAsync(assetId, "title", "Waiting For Artwork Verdict");
        await InsertCanonicalAsync(workId, "cover_state", "missing");
        await InsertCanonicalAsync(workId, "cover_source", "none");

        var repo = new LibraryItemRepository(_db);

        var beforePage = await repo.GetPageAsync(new LibraryItemQuery());
        Assert.Empty(beforePage.Items);

        var beforeDetail = await repo.GetDetailAsync(workId);
        Assert.NotNull(beforeDetail);
        Assert.Equal("hidden", beforeDetail!.LibraryVisibility);
        Assert.False(beforeDetail.IsReadyForLibrary);
        Assert.Equal("missing", beforeDetail.ArtworkState);
        Assert.Null(beforeDetail.CoverUrl);

        await InsertCanonicalAsync(workId, "artwork_settled_at", DateTimeOffset.UtcNow.ToString("O"));

        var afterPage = await repo.GetPageAsync(new LibraryItemQuery());
        var item = Assert.Single(afterPage.Items);
        Assert.Equal(workId, item.EntityId);
        Assert.Equal("visible", item.LibraryVisibility);
        Assert.True(item.IsReadyForLibrary);
        Assert.Equal("missing", item.ArtworkState);
        Assert.Null(item.CoverUrl);
    }

    [Fact]
    public async Task LibraryItemProjection_ExposesCuratorLifecycleStatesOnlyWhenRequested()
    {
        var settledAt = DateTimeOffset.UtcNow.ToString("O");

        var (identifiedWorkId, identifiedAssetId) = await BuildStandaloneWorkAsync("Books");
        await InsertCanonicalAsync(identifiedAssetId, "title", "Identified Item");
        await InsertCanonicalAsync(identifiedWorkId, "wikidata_qid", "Q777");
        await InsertCanonicalAsync(identifiedWorkId, "cover_state", "present");
        await InsertCanonicalAsync(identifiedWorkId, "artwork_settled_at", settledAt);

        var (provisionalWorkId, provisionalAssetId) = await BuildStandaloneWorkAsync("Movies");
        await InsertCanonicalAsync(provisionalAssetId, "title", "Provisional Item");
        await InsertCanonicalAsync(provisionalWorkId, "cover_state", "present");
        await InsertCanonicalAsync(provisionalWorkId, "artwork_settled_at", settledAt);
        await UpdateWorkCuratorStateAsync(provisionalWorkId, "provisional");

        var (rejectedWorkId, rejectedAssetId) = await BuildStandaloneWorkAsync("Music");
        await InsertCanonicalAsync(rejectedAssetId, "title", "Rejected Item");
        await InsertCanonicalAsync(rejectedWorkId, "cover_state", "present");
        await InsertCanonicalAsync(rejectedWorkId, "artwork_settled_at", settledAt);
        await UpdateWorkCuratorStateAsync(rejectedWorkId, "rejected");

        var repo = new LibraryItemRepository(_db);

        var visiblePage = await repo.GetPageAsync(new LibraryItemQuery());
        Assert.Contains(visiblePage.Items, item => item.EntityId == identifiedWorkId);
        Assert.DoesNotContain(visiblePage.Items, item => item.EntityId == provisionalWorkId);
        Assert.DoesNotContain(visiblePage.Items, item => item.EntityId == rejectedWorkId);

        var allPage = await repo.GetPageAsync(new LibraryItemQuery(IncludeAll: true));
        Assert.Contains(allPage.Items, item => item.EntityId == identifiedWorkId && item.Status == "Confirmed");
        Assert.Contains(allPage.Items, item => item.EntityId == provisionalWorkId && item.Status == "Provisional");
        Assert.Contains(allPage.Items, item => item.EntityId == rejectedWorkId && item.Status == "Rejected");
    }

    private async Task<(Guid WorkId, Guid AssetId)> BuildStandaloneWorkAsync(string mediaType)
    {
        using var conn = _db.CreateConnection();
        var collectionId = Guid.NewGuid();
        var workId = Guid.NewGuid();
        var editionId = Guid.NewGuid();
        var assetId = Guid.NewGuid();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            INSERT INTO collections (id, created_at) VALUES ('{collectionId}', datetime('now'));
            INSERT INTO works (id, collection_id, media_type)
                VALUES ('{workId}', '{collectionId}', '{mediaType}');
            INSERT INTO editions (id, work_id) VALUES ('{editionId}', '{workId}');
            INSERT INTO media_assets (id, edition_id, content_hash, file_path_root, status)
                VALUES ('{assetId}', '{editionId}', 'hash_{assetId:N}', '/library/{assetId:N}.bin', 'Normal');
            """;
        await cmd.ExecuteNonQueryAsync();

        return (workId, assetId);
    }

    private async Task InsertCanonicalAsync(Guid entityId, string key, string value)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR REPLACE INTO canonical_values (entity_id, key, value, last_scored_at)
            VALUES (@entityId, @key, @value, datetime('now'));
            """;
        cmd.Parameters.AddWithValue("@entityId", entityId.ToString());
        cmd.Parameters.AddWithValue("@key", key);
        cmd.Parameters.AddWithValue("@value", value);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task UpdateWorkCuratorStateAsync(Guid workId, string state)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE works
            SET curator_state = @state
            WHERE id = @workId;
            """;
        cmd.Parameters.AddWithValue("@workId", workId.ToString());
        cmd.Parameters.AddWithValue("@state", state);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task CreateIdentityJobAsync(Guid assetId, IdentityJobState state)
    {
        var repo = new IdentityJobRepository(_db);
        await repo.CreateAsync(new IdentityJob
        {
            EntityId = assetId,
            EntityType = "MediaAsset",
            MediaType = "Books",
            State = state.ToString(),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
    }
}
