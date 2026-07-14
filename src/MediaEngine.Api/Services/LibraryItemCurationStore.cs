using System.Data;
using System.Text.Json.Serialization;
using Dapper;
using MediaEngine.Api.Models;
using MediaEngine.Domain;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Storage;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Api.Services;

public interface ILibraryItemCurationStore
{
    Task<LibraryItemTarget?> ResolveTargetAsync(Guid entityId, CancellationToken ct = default);
    Task<IReadOnlyDictionary<Guid, LibraryItemTarget>> ResolveWorkTargetsAsync(
        IReadOnlyCollection<Guid> workIds,
        CancellationToken ct = default);
    Task UpsertCanonicalValuesAsync(
        Guid entityId,
        IReadOnlyCollection<MetadataClaim> claims,
        CancellationToken ct = default);
    Task MarkWorkRegisteredAsync(Guid workId, CancellationToken ct = default);
    Task CompletePendingReviewsAsync(
        Guid assetId,
        Guid workId,
        string status,
        string resolvedBy,
        DateTimeOffset resolvedAt,
        CancellationToken ct = default);
    Task<IReadOnlyDictionary<Guid, LibraryItemRemovalTarget>> GetRemovalTargetsAsync(
        IReadOnlyCollection<Guid> workIds,
        CancellationToken ct = default);
    Task DeleteWorkRecordsAsync(LibraryItemRemovalTarget target, CancellationToken ct = default);
    Task<int> ApproveWorksAsync(IReadOnlyCollection<Guid> workIds, DateTimeOffset now, CancellationToken ct = default);
    Task MarkRejectedAsync(
        LibraryItemTarget target,
        string newFilePath,
        DateTimeOffset now,
        CancellationToken ct = default);
    Task<LibraryItemRecoveryResult?> RecoverAsync(Guid workId, DateTimeOffset now, CancellationToken ct = default);
    Task<LibraryItemProvisionalResult?> MarkProvisionalAsync(
        Guid workId,
        ProvisionalMetadataRequest metadata,
        DateTimeOffset now,
        CancellationToken ct = default);
    Task<IReadOnlyList<LibraryItemHistoryEntry>> GetHistoryAsync(Guid workId, CancellationToken ct = default);
}

/// <summary>
/// Persistence boundary for curator-driven library item operations. All commands use
/// short-lived connections, explicit transactions for multi-table state changes, and
/// guid-blob-v1-safe parameters.
/// </summary>
public sealed class LibraryItemCurationStore(IDatabaseConnection db) : ILibraryItemCurationStore
{
    public async Task<LibraryItemTarget?> ResolveTargetAsync(Guid entityId, CancellationToken ct = default)
    {
        using var connection = db.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<LibraryItemTarget>(new CommandDefinition(
            TargetSelect + "\n" + """
                WHERE ma.id = @entityId
                   OR e.work_id = @entityId
                ORDER BY CASE WHEN ma.id = @entityId THEN 0 ELSE 1 END, ma.file_path_root
                LIMIT 1;
                """,
            new { entityId },
            cancellationToken: ct));
    }

    public async Task<IReadOnlyDictionary<Guid, LibraryItemTarget>> ResolveWorkTargetsAsync(
        IReadOnlyCollection<Guid> workIds,
        CancellationToken ct = default)
    {
        if (workIds.Count == 0)
            return new Dictionary<Guid, LibraryItemTarget>();

        using var connection = db.CreateConnection();
        var rows = await connection.QueryAsync<LibraryItemTarget>(new CommandDefinition(
            TargetSelect + "\n" + """
                WHERE e.work_id IN @workIds
                ORDER BY e.work_id, ma.file_path_root;
                """,
            new { workIds = ToBlobArray(workIds) },
            cancellationToken: ct));

        return rows
            .GroupBy(row => row.WorkId)
            .ToDictionary(group => group.Key, group => group.First());
    }

    public async Task UpsertCanonicalValuesAsync(
        Guid entityId,
        IReadOnlyCollection<MetadataClaim> claims,
        CancellationToken ct = default)
    {
        if (claims.Count == 0)
            return;

        using var connection = db.CreateConnection();
        using var transaction = connection.BeginTransaction();
        try
        {
            foreach (var claim in claims)
            {
                await connection.ExecuteAsync(new CommandDefinition("""
                    INSERT INTO canonical_values (entity_id, key, value, last_scored_at, is_conflicted, needs_review)
                    VALUES (@entityId, @key, @value, @scoredAt, 0, 0)
                    ON CONFLICT(entity_id, key) DO UPDATE SET
                        value          = excluded.value,
                        last_scored_at = excluded.last_scored_at,
                        is_conflicted  = 0,
                        needs_review   = 0;
                    """,
                    new
                    {
                        entityId,
                        key = claim.ClaimKey,
                        value = claim.ClaimValue,
                        scoredAt = claim.ClaimedAt.ToString("O"),
                    },
                    transaction,
                    cancellationToken: ct));
            }

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task MarkWorkRegisteredAsync(Guid workId, CancellationToken ct = default)
    {
        using var connection = db.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE works
            SET curator_state = 'registered', rejected_at = NULL
            WHERE id = @workId;
            """, new { workId }, cancellationToken: ct));
    }

    public async Task CompletePendingReviewsAsync(
        Guid assetId,
        Guid workId,
        string status,
        string resolvedBy,
        DateTimeOffset resolvedAt,
        CancellationToken ct = default)
    {
        using var connection = db.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE review_queue
            SET status = @status, resolved_at = @resolvedAt, resolved_by = @resolvedBy
            WHERE entity_id IN (@assetId, @workId) AND status = 'Pending';
            """,
            new { assetId, workId, status, resolvedAt = resolvedAt.ToString("O"), resolvedBy },
            cancellationToken: ct));
    }

    public async Task<IReadOnlyDictionary<Guid, LibraryItemRemovalTarget>> GetRemovalTargetsAsync(
        IReadOnlyCollection<Guid> workIds,
        CancellationToken ct = default)
    {
        if (workIds.Count == 0)
            return new Dictionary<Guid, LibraryItemRemovalTarget>();

        using var connection = db.CreateConnection();
        var parameters = new { workIds = ToBlobArray(workIds) };
        var rows = (await connection.QueryAsync<RemovalRow>(new CommandDefinition("""
            SELECT w.id AS WorkId,
                   w.collection_id AS CollectionId,
                   w.parent_work_id AS ParentWorkId,
                   ma.file_path_root AS FilePath,
                   (
                       SELECT cv.value
                       FROM canonical_values cv
                       WHERE cv.entity_id IN (w.id, ma.id)
                         AND cv.key IN ('title', 'show_name', 'episode_title')
                         AND NULLIF(cv.value, '') IS NOT NULL
                       ORDER BY CASE cv.key WHEN 'title' THEN 0 WHEN 'show_name' THEN 1 ELSE 2 END
                       LIMIT 1
                   ) AS Title
            FROM works w
            INNER JOIN editions e ON e.work_id = w.id
            INNER JOIN media_assets ma ON ma.edition_id = e.id
            WHERE w.id IN @workIds
            ORDER BY w.id, ma.file_path_root;
            """, parameters, cancellationToken: ct))).ToList();

        var assetRows = await connection.QueryAsync<ManagedAssetPathRow>(new CommandDefinition("""
            SELECT entity_id AS WorkId,
                   local_image_path AS LocalImagePath,
                   local_image_path_s AS LocalImagePathSmall,
                   local_image_path_m AS LocalImagePathMedium,
                   local_image_path_l AS LocalImagePathLarge
            FROM entity_assets
            WHERE entity_id IN @workIds;
            """, parameters, cancellationToken: ct));

        var managedPaths = assetRows
            .GroupBy(row => row.WorkId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<string>)group
                    .SelectMany(row => new[]
                    {
                        row.LocalImagePath,
                        row.LocalImagePathSmall,
                        row.LocalImagePathMedium,
                        row.LocalImagePathLarge,
                    })
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Select(path => path!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList());

        return rows
            .GroupBy(row => row.WorkId)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var first = group.First();
                    return new LibraryItemRemovalTarget(
                        first.WorkId,
                        first.CollectionId,
                        first.ParentWorkId,
                        group.Select(row => row.FilePath)
                            .Where(path => !string.IsNullOrWhiteSpace(path))
                            .Select(path => path!)
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList(),
                        managedPaths.GetValueOrDefault(first.WorkId) ?? [],
                        group.Select(row => row.Title).FirstOrDefault(title => !string.IsNullOrWhiteSpace(title)));
                });
    }

    public async Task DeleteWorkRecordsAsync(LibraryItemRemovalTarget target, CancellationToken ct = default)
    {
        using var connection = db.CreateConnection();
        using var transaction = connection.BeginTransaction();
        try
        {
            await connection.ExecuteAsync(new CommandDefinition("""
                DELETE FROM review_queue
                WHERE entity_id IN (
                    SELECT ma.id
                    FROM editions e
                    INNER JOIN media_assets ma ON ma.edition_id = e.id
                    WHERE e.work_id = @workId
                );
                """, new { workId = target.WorkId }, transaction, cancellationToken: ct));

            await connection.ExecuteAsync(new CommandDefinition(
                "DELETE FROM entity_assets WHERE entity_id = @workId;",
                new { workId = target.WorkId },
                transaction,
                cancellationToken: ct));

            await connection.ExecuteAsync(new CommandDefinition(
                "DELETE FROM works WHERE id = @workId;",
                new { workId = target.WorkId },
                transaction,
                cancellationToken: ct));

            if (target.CollectionId.HasValue)
            {
                await connection.ExecuteAsync(new CommandDefinition("""
                    DELETE FROM collections
                    WHERE id = @collectionId
                      AND NOT EXISTS (SELECT 1 FROM works WHERE collection_id = @collectionId);
                    """,
                    new { collectionId = target.CollectionId.Value },
                    transaction,
                    cancellationToken: ct));
            }

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task<int> ApproveWorksAsync(
        IReadOnlyCollection<Guid> workIds,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        if (workIds.Count == 0)
            return 0;

        using var connection = db.CreateConnection();
        using var transaction = connection.BeginTransaction();
        var parameters = new { workIds = ToBlobArray(workIds), now = now.ToString("O") };
        try
        {
            var processed = await connection.ExecuteAsync(new CommandDefinition("""
                UPDATE works
                SET wikidata_status = 'missing', wikidata_checked_at = @now
                WHERE id IN @workIds;
                """, parameters, transaction, cancellationToken: ct));

            await connection.ExecuteAsync(new CommandDefinition("""
                UPDATE review_queue
                SET status = 'Resolved', resolved_at = @now, resolved_by = 'user:curator'
                WHERE status = 'Pending'
                  AND entity_id IN (
                      SELECT ma.id
                      FROM editions e
                      INNER JOIN media_assets ma ON ma.edition_id = e.id
                      WHERE e.work_id IN @workIds
                  );
                """, parameters, transaction, cancellationToken: ct));

            transaction.Commit();
            return processed;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task MarkRejectedAsync(
        LibraryItemTarget target,
        string newFilePath,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        using var connection = db.CreateConnection();
        using var transaction = connection.BeginTransaction();
        var parameters = new
        {
            target.AssetId,
            target.WorkId,
            path = newFilePath,
            now = now.ToString("O"),
        };
        try
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE media_assets SET file_path_root = @path WHERE id = @AssetId;",
                parameters,
                transaction,
                cancellationToken: ct));
            await connection.ExecuteAsync(new CommandDefinition("""
                UPDATE review_queue
                SET status = 'Dismissed', resolved_at = @now, resolved_by = 'user:reject'
                WHERE entity_id IN (@AssetId, @WorkId) AND status = 'Pending';
                """, parameters, transaction, cancellationToken: ct));
            await connection.ExecuteAsync(new CommandDefinition("""
                UPDATE works
                SET curator_state = 'rejected', rejected_at = @now
                WHERE id = @WorkId;
                """, parameters, transaction, cancellationToken: ct));
            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task<LibraryItemRecoveryResult?> RecoverAsync(
        Guid workId,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        using var connection = db.CreateConnection();
        using var transaction = connection.BeginTransaction();
        try
        {
            var affected = await connection.ExecuteAsync(new CommandDefinition("""
                UPDATE works
                SET curator_state = NULL, rejected_at = NULL
                WHERE id = @workId AND curator_state = 'rejected';
                """, new { workId }, transaction, cancellationToken: ct));
            if (affected == 0)
            {
                transaction.Rollback();
                return null;
            }

            var assetId = await connection.QueryFirstOrDefaultAsync<Guid?>(new CommandDefinition("""
                SELECT ma.id
                FROM editions e
                INNER JOIN media_assets ma ON ma.edition_id = e.id
                WHERE e.work_id = @workId
                ORDER BY ma.file_path_root
                LIMIT 1;
                """, new { workId }, transaction, cancellationToken: ct));

            Guid? reviewId = null;
            if (assetId.HasValue)
            {
                reviewId = Guid.NewGuid();
                await connection.ExecuteAsync(new CommandDefinition("""
                    INSERT INTO review_queue
                        (id, entity_id, entity_type, trigger, status, confidence_score, detail, created_at)
                    VALUES
                        (@reviewId, @assetId, 'MediaAsset', 'UserFixMatch', 'Pending', 0,
                         'Un-rejected by user - returned to review queue.', @createdAt);
                    """,
                    new { reviewId = reviewId.Value, assetId = assetId.Value, createdAt = now.ToString("O") },
                    transaction,
                    cancellationToken: ct));
            }

            transaction.Commit();
            return new LibraryItemRecoveryResult(workId, assetId, reviewId);
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task<LibraryItemProvisionalResult?> MarkProvisionalAsync(
        Guid workId,
        ProvisionalMetadataRequest metadata,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        using var connection = db.CreateConnection();
        using var transaction = connection.BeginTransaction();
        try
        {
            var metadataJson = System.Text.Json.JsonSerializer.Serialize(metadata);
            var affected = await connection.ExecuteAsync(new CommandDefinition("""
                UPDATE works
                SET curator_state = 'provisional', provisional_metadata_json = @metadataJson
                WHERE id = @workId;
                """, new { workId, metadataJson }, transaction, cancellationToken: ct));
            if (affected == 0)
            {
                transaction.Rollback();
                return null;
            }

            var assetId = await connection.QueryFirstOrDefaultAsync<Guid?>(new CommandDefinition("""
                SELECT ma.id
                FROM editions e
                INNER JOIN media_assets ma ON ma.edition_id = e.id
                WHERE e.work_id = @workId
                ORDER BY ma.file_path_root
                LIMIT 1;
                """, new { workId }, transaction, cancellationToken: ct));

            var claimsWritten = 0;
            if (assetId.HasValue)
            {
                await connection.ExecuteAsync(new CommandDefinition("""
                    UPDATE review_queue
                    SET status = 'Dismissed', resolved_at = @now, resolved_by = 'user:provisional'
                    WHERE entity_id = @assetId AND status = 'Pending';
                    """,
                    new { assetId = assetId.Value, now = now.ToString("O") },
                    transaction,
                    cancellationToken: ct));

                var fields = BuildProvisionalFields(metadata);
                foreach (var (key, value) in fields)
                {
                    if (string.IsNullOrWhiteSpace(value))
                        continue;

                    var parameters = new
                    {
                        id = Guid.NewGuid(),
                        entityId = assetId.Value,
                        key,
                        value,
                        providerId = WellKnownProviders.LocalProcessor,
                        now = now.ToString("O"),
                    };
                    await connection.ExecuteAsync(new CommandDefinition("""
                        INSERT INTO metadata_claims
                            (id, entity_id, claim_key, claim_value, provider_id, confidence, is_user_locked, claimed_at)
                        VALUES
                            (@id, @entityId, @key, @value, @providerId, 1.0, 1, @now);

                        INSERT INTO canonical_values
                            (entity_id, key, value, winning_provider_id, is_conflicted, needs_review, last_scored_at)
                        VALUES (@entityId, @key, @value, @providerId, 0, 0, @now)
                        ON CONFLICT(entity_id, key) DO UPDATE SET
                            value = excluded.value,
                            winning_provider_id = excluded.winning_provider_id,
                            is_conflicted = 0,
                            needs_review = 0,
                            last_scored_at = excluded.last_scored_at;
                        """, parameters, transaction, cancellationToken: ct));
                    claimsWritten++;
                }
            }

            transaction.Commit();
            return new LibraryItemProvisionalResult(workId, assetId, claimsWritten);
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task<IReadOnlyList<LibraryItemHistoryEntry>> GetHistoryAsync(
        Guid workId,
        CancellationToken ct = default)
    {
        using var connection = db.CreateConnection();
        var assetId = await connection.QueryFirstOrDefaultAsync<Guid?>(new CommandDefinition("""
            SELECT ma.id
            FROM editions e
            INNER JOIN media_assets ma ON ma.edition_id = e.id
            WHERE e.work_id = @workId
            ORDER BY ma.file_path_root
            LIMIT 1;
            """, new { workId }, cancellationToken: ct));

        var rows = await connection.QueryAsync<HistoryRow>(new CommandDefinition("""
            SELECT id AS Id,
                   occurred_at AS OccurredAt,
                   action_type AS EventType,
                   entity_id AS EntityId,
                   detail AS Detail
            FROM system_activity
            WHERE entity_id = @workId OR entity_id = @assetId
            ORDER BY occurred_at DESC
            LIMIT 200;
            """,
            new { workId, assetId = assetId ?? workId },
            cancellationToken: ct));

        return rows.Select(row => new LibraryItemHistoryEntry(
            row.Id.ToString(),
            row.EntityId ?? workId,
            row.OccurredAt,
            row.EventType ?? string.Empty,
            FormatActionTypeLabel(row.EventType ?? string.Empty),
            row.Detail)).ToList();
    }

    private static IReadOnlyDictionary<string, string?> BuildProvisionalFields(ProvisionalMetadataRequest metadata) =>
        new Dictionary<string, string?>
        {
            [MetadataFieldConstants.Title] = metadata.Title,
            [MetadataFieldConstants.Author] = metadata.Creator,
            [MetadataFieldConstants.Year] = metadata.Year,
            [MetadataFieldConstants.Description] = metadata.Description,
            ["narrator"] = metadata.Narrator,
            [BridgeIdKeys.Isbn] = metadata.Isbn,
            ["director"] = metadata.Director,
            [MetadataFieldConstants.Runtime] = metadata.Runtime,
            ["host"] = metadata.Host,
            ["writer"] = metadata.Writer,
            [MetadataFieldConstants.Artist] = metadata.Artist,
        };

    private static byte[][] ToBlobArray(IEnumerable<Guid> values) => values.Select(GuidSql.ToBlob).ToArray();

    private const string TargetSelect = """
        SELECT ma.id AS AssetId,
               e.work_id AS WorkId,
               ma.file_path_root AS FilePath,
               (
                   SELECT cv.value
                   FROM canonical_values cv
                   WHERE cv.entity_id IN (ma.id, e.work_id)
                     AND cv.key IN ('title', 'show_name', 'episode_title')
                     AND NULLIF(cv.value, '') IS NOT NULL
                   ORDER BY CASE cv.key WHEN 'title' THEN 0 WHEN 'show_name' THEN 1 ELSE 2 END
                   LIMIT 1
               ) AS Title,
               (
                   SELECT cv.value
                   FROM canonical_values cv
                   WHERE cv.entity_id = ma.id
                     AND cv.key = 'media_type'
                     AND NULLIF(cv.value, '') IS NOT NULL
                   LIMIT 1
               ) AS MediaType
        FROM media_assets ma
        INNER JOIN editions e ON e.id = ma.edition_id
        """;

    private static string FormatActionTypeLabel(string actionType) => actionType switch
    {
        "FileDetected" => "File detected",
        "FileIngested" => "File ingested",
        "MetadataExtracted" => "Metadata extracted",
        "ConfidenceScored" => "Confidence scored",
        "MovedToStaging" => "Moved to staging",
        "Promoted" => "Promoted to library",
        "ReviewItemCreated" => "Sent for review",
        "ReviewItemResolved" => "Review resolved",
        "HydrationStarted" => "Enrichment started",
        "HydrationCompleted" => "Enrichment complete",
        "WikidataMatched" => "Identified on Wikidata",
        "WikidataMatchFailed" => "No Wikidata match found",
        "RetailEnriched" => "Cover art retrieved",
        "RetailEnrichFailed" => "No cover art found",
        "MetadataManualOverride" => "Manual metadata override",
        "MetadataWrittenToFile" => "Metadata written to file",
        "CoverArtSaved" => "Cover art saved",
        "HeroBannerGenerated" => "Hero banner generated",
        "CollectionCreated" => "Collection created",
        "CollectionAssigned" => "Assigned to collection",
        "PersonHydrated" => "Person enriched",
        "FileRejected" => "Rejected",
        "Recovered" => "Recovered from rejection",
        "FileHashed" => "Content fingerprinted",
        "DuplicateSkipped" => "Duplicate skipped",
        "EntityChainCreated" => "Library records created",
        "HydrationEnqueued" => "Queued for enrichment",
        _ => actionType.Replace("_", " "),
    };

    private sealed class RemovalRow
    {
        public Guid WorkId { get; init; }
        public Guid? CollectionId { get; init; }
        public Guid? ParentWorkId { get; init; }
        public string? FilePath { get; init; }
        public string? Title { get; init; }
    }

    private sealed class ManagedAssetPathRow
    {
        public Guid WorkId { get; init; }
        public string? LocalImagePath { get; init; }
        public string? LocalImagePathSmall { get; init; }
        public string? LocalImagePathMedium { get; init; }
        public string? LocalImagePathLarge { get; init; }
    }

    private sealed class HistoryRow
    {
        public long Id { get; init; }
        public DateTimeOffset OccurredAt { get; init; }
        public string? EventType { get; init; }
        public Guid? EntityId { get; init; }
        public string? Detail { get; init; }
    }
}

public sealed class LibraryItemTarget
{
    public Guid AssetId { get; init; }
    public Guid WorkId { get; init; }
    public string? FilePath { get; init; }
    public string? Title { get; init; }
    public string? MediaType { get; init; }
}

public sealed record LibraryItemRemovalTarget(
    Guid WorkId,
    Guid? CollectionId,
    Guid? ParentWorkId,
    IReadOnlyList<string> FilePaths,
    IReadOnlyList<string> ManagedAssetPaths,
    string? Title);

public sealed record LibraryItemRecoveryResult(Guid WorkId, Guid? AssetId, Guid? ReviewId);

public sealed record LibraryItemProvisionalResult(Guid WorkId, Guid? AssetId, int ClaimsWritten);

public sealed record LibraryItemHistoryEntry(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("entity_id")] Guid EntityId,
    [property: JsonPropertyName("occurred_at")] DateTimeOffset OccurredAt,
    [property: JsonPropertyName("event_type")] string EventType,
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("detail")] string? Detail);
