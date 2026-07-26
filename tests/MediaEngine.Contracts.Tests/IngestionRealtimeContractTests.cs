using System.Text.Json;
using System.Text.Json.Nodes;
using MediaEngine.Contracts.Ingestion;
using MediaEngine.Contracts.Maintenance;
using MediaEngine.Contracts.Metadata;
using MediaEngine.Contracts.Realtime;

namespace MediaEngine.Contracts.Tests;

public sealed class IngestionRealtimeContractTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void IngestionHttpContracts_PreserveSnakeCaseWireFields()
    {
        AssertKeys(
            new RescanRequest
            {
                RootPath = "D:/watch",
                IncludeSubdirectories = true,
            },
            "root_path",
            "include_subdirectories");

        AssertKeys(
            new ScanResponse
            {
                Operations =
                [
                    new PendingOperationDto
                    {
                        SourcePath = "a",
                        DestinationPath = "b",
                        OperationKind = "move",
                    },
                ],
            },
            "operations",
            "total_count");

        AssertKeys(
            new WatchFolderPageResponse
            {
                WatchDirectory = "D:/watch",
                Files = [new WatchFolderFileDto()],
                Offset = 0,
                Limit = 100,
                HasMore = true,
                NextCursor = "100",
            },
            "watch_directory",
            "files",
            "offset",
            "limit",
            "has_more",
            "next_cursor");
    }

    [Fact]
    public void Pass2RetagAndStorageMaintenance_PreserveExistingWireFields()
    {
        AssertKeys(
            new DeferredEnrichmentStatusResponse(3, true),
            "pending_count",
            "two_pass_enabled");

        AssertKeys(
            new RetagSweepStateResponse(
                true,
                [new RetagSweepPendingDiffEntry("books", ["title"], ["year"])],
                new Dictionary<string, string>()),
            "has_pending_diff",
            "pending_diff",
            "current_hashes");

        AssertKeys(
            new StorageMaintenanceResultDto(
                DateTimeOffset.UnixEpoch,
                DateTimeOffset.UnixEpoch,
                false,
                [new StorageMaintenanceStepResultDto("cache", 2, "done")]),
            "startedAt",
            "completedAt",
            "dryRun",
            "steps",
            "totalAffectedRows");
    }

    [Fact]
    public void InitialSweepAndFolderHealth_PreserveSnakeCaseSignalRFields()
    {
        AssertKeys(
            new InitialSweepProgressEvent(10, 5, 3, 1, 1, 1024),
            "discovered",
            "processed",
            "hashed",
            "cached",
            "failed",
            "bytes_hashed");

        AssertKeys(
            new InitialSweepCompletedEvent(
                10,
                7,
                2,
                1,
                2048,
                1.5,
                DateTimeOffset.UnixEpoch),
            "discovered",
            "hashed",
            "cached",
            "failed",
            "bytes_hashed",
            "elapsed_seconds",
            "completed_at");

        AssertKeys(
            new FolderHealthChangedEvent(
                "D:/watch",
                true,
                true,
                false,
                DateTimeOffset.UnixEpoch),
            "path",
            "is_accessible",
            "has_read",
            "has_write",
            "checked_at");
    }

    [Fact]
    public void CanonicalAndAnonymousReplacementEvents_KeepTheirDistinctShapes()
    {
        AssertKeys(
            new MetadataHarvestedEvent(Guid.Empty, "provider", ["title"]),
            "entityId",
            "providerName",
            "updatedFields");

        AssertKeys(
            new ManualMetadataHarvestedEvent(Guid.Empty, "user_manual", ["title"]),
            "entity_id",
            "provider_name",
            "updated_fields");

        AssertKeys(
            new ModelDownloadProgressEvent("Text", 50, 100, 200),
            "role",
            "percent",
            "bytesDownloaded",
            "totalBytes");

        AssertKeys(
            new RetagSweepCompletedEvent(10, 8, 1, 1),
            "processed",
            "succeeded",
            "transient",
            "terminal");
    }

    private static void AssertKeys<T>(T value, params string[] expected)
    {
        var node = JsonSerializer.SerializeToNode(value, JsonOptions);
        var json = Assert.IsType<JsonObject>(node);
        Assert.Equal(expected.Order(StringComparer.Ordinal), json.Select(pair => pair.Key).Order(StringComparer.Ordinal));
    }
}
