using MediaEngine.Application.ReadModels;
using MediaEngine.Application.Services;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Api.Services.ReadServices;

public sealed class IngestionBatchReadService : IIngestionBatchReadService
{
    private readonly IDatabaseConnection _db;

    public IngestionBatchReadService(IDatabaseConnection db)
    {
        _db = db;
    }

    public Task<IReadOnlyList<IngestionBatchItemResponse>> GetItemsAsync(
        Guid batchId,
        int offset,
        int limit,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = Query;
        cmd.Parameters.AddWithValue("@batchId", batchId.ToString());
        cmd.Parameters.AddWithValue("@offset", Math.Max(0, offset));
        cmd.Parameters.AddWithValue("@limit", Math.Clamp(limit, 1, 501));

        using var reader = cmd.ExecuteReader();
        var items = new List<IngestionBatchItemResponse>();
        while (reader.Read())
        {
            var filePath = reader.GetString(1);
            var status = reader.IsDBNull(4) ? "detected" : reader.GetString(4);
            var identityState = reader.IsDBNull(11) ? null : reader.GetString(11);
            var stage = ResolveItemStage(status, identityState);
            var totalPeople = reader.GetInt32(12);
            var enrichedPeople = reader.GetInt32(13);
            var stage3CoreDone = reader.GetInt32(14) == 1;
            var stage3EnhancersDone = reader.GetInt32(15) == 1;
            var progressPercent = ResolveItemProgressPercent(
                stage,
                identityState,
                totalPeople,
                enrichedPeople,
                stage3CoreDone,
                stage3EnhancersDone);

            items.Add(new IngestionBatchItemResponse
            {
                Id = Guid.Parse(reader.GetString(0)),
                FilePath = filePath,
                FileName = Path.GetFileName(filePath),
                MediaAssetId = reader.IsDBNull(2) ? null : Guid.Parse(reader.GetString(2)),
                ContentHash = reader.IsDBNull(3) ? null : reader.GetString(3),
                Status = status,
                MediaType = reader.IsDBNull(5) ? null : reader.GetString(5),
                ConfidenceScore = reader.IsDBNull(6) ? null : reader.GetDouble(6),
                DetectedTitle = reader.IsDBNull(7) ? null : reader.GetString(7),
                ErrorDetail = reader.IsDBNull(8) ? null : reader.GetString(8),
                CreatedAt = DateTimeOffset.Parse(reader.GetString(9)),
                UpdatedAt = DateTimeOffset.Parse(reader.GetString(10)),
                IdentityState = identityState,
                Stage = stage,
                StageOrder = ResolveItemStageOrder(stage),
                ProgressPercent = progressPercent,
                WorkUnitsTotal = ResolveItemWorkUnitsTotal(identityState, totalPeople),
                WorkUnitsCompleted = ResolveItemWorkUnitsCompleted(identityState, totalPeople, enrichedPeople, stage3CoreDone, stage3EnhancersDone),
                IsTerminal = progressPercent >= 100,
            });
        }

        return Task.FromResult<IReadOnlyList<IngestionBatchItemResponse>>(items);
    }

    private static string ResolveItemStage(string status, string? identityState)
    {
        if (string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase))
            return "failed";
        if (string.Equals(status, "needs_review", StringComparison.OrdinalIgnoreCase))
            return "needs_review";

        return identityState switch
        {
            "Queued" => "queued_identity",
            "RetailSearching" or "RetailMatched" or "RetailMatchedNeedsReview" => "identifying",
            "BridgeSearching" or "QidResolved" or "Hydrating" => "hydrating",
            "UniverseEnriching" => "universe_enriching",
            "Ready" or "ReadyWithoutUniverse" or "Completed" => "complete",
            "RetailNoMatch" or "QidNoMatch" or "QidNeedsReview" => "needs_review",
            "Failed" => "failed",
            _ => status,
        };
    }

    private static int ResolveItemStageOrder(string stage) => stage switch
    {
        "detected" => 0,
        "hashing" => 1,
        "processed" => 2,
        "scored" => 3,
        "registered" => 4,
        "queued_identity" => 5,
        "identifying" => 6,
        "hydrating" => 7,
        "universe_enriching" => 8,
        "complete" or "needs_review" or "failed" => 9,
        _ => 0,
    };

    private static int ResolveItemProgressPercent(
        string stage,
        string? identityState,
        int totalPeople,
        int enrichedPeople,
        bool stage3CoreDone,
        bool stage3EnhancersDone)
    {
        if (string.Equals(identityState, "Hydrating", StringComparison.OrdinalIgnoreCase))
        {
            var personProgress = totalPeople <= 0
                ? 1.0
                : Math.Clamp(enrichedPeople / (double)totalPeople, 0, 1);
            return (int)Math.Round(50 + (20 * personProgress));
        }

        if (string.Equals(identityState, "UniverseEnriching", StringComparison.OrdinalIgnoreCase))
        {
            var progress = 80;
            if (stage3CoreDone) progress += 10;
            if (stage3EnhancersDone) progress += 10;
            return progress;
        }

        return stage switch
        {
            "detected" => 5,
            "hashing" => 15,
            "processed" => 35,
            "scored" => 55,
            "registered" => 70,
            "queued_identity" => 10,
            "identifying" => 30,
            "hydrating" => 50,
            "universe_enriching" => 80,
            "complete" or "needs_review" or "failed" => 100,
            _ => 0,
        };
    }

    private static int ResolveItemWorkUnitsTotal(string? identityState, int totalPeople) =>
        identityState switch
        {
            "Hydrating" => Math.Max(totalPeople, 1),
            "UniverseEnriching" => 2,
            _ => 1,
        };

    private static int ResolveItemWorkUnitsCompleted(
        string? identityState,
        int totalPeople,
        int enrichedPeople,
        bool stage3CoreDone,
        bool stage3EnhancersDone) =>
        identityState switch
        {
            "Hydrating" => totalPeople <= 0 ? 1 : Math.Clamp(enrichedPeople, 0, totalPeople),
            "UniverseEnriching" => (stage3CoreDone ? 1 : 0) + (stage3EnhancersDone ? 1 : 0),
            "Ready" or "ReadyWithoutUniverse" or "Completed" or "RetailNoMatch" or "QidNoMatch" or "QidNeedsReview" or "Failed" => 1,
            _ => 0,
        };

    private const string Query = """
        WITH latest_jobs AS (
            SELECT
                entity_id,
                state,
                updated_at,
                ROW_NUMBER() OVER (
                    PARTITION BY entity_id
                    ORDER BY updated_at DESC, created_at DESC
                ) AS rn
            FROM identity_jobs
            WHERE ingestion_run_id = @batchId
        ),
        person_counts AS (
            SELECT
                pml.media_asset_id AS entity_id,
                COUNT(DISTINCT p.id) AS total_people,
                COUNT(DISTINCT CASE WHEN p.enriched_at IS NOT NULL THEN p.id END) AS enriched_people
            FROM person_media_links pml
            INNER JOIN persons p ON p.id = pml.person_id
            GROUP BY pml.media_asset_id
        ),
        canonical_flags AS (
            SELECT
                entity_id,
                MAX(CASE WHEN key = 'stage3_enriched_at' THEN 1 ELSE 0 END) AS stage3_core_done,
                MAX(CASE WHEN key = 'stage3_enhanced_at' THEN 1 ELSE 0 END) AS stage3_enhancers_done
            FROM canonical_values
            WHERE key IN ('stage3_enriched_at', 'stage3_enhanced_at')
            GROUP BY entity_id
        )
        SELECT
            il.id,
            il.file_path,
            il.media_asset_id,
            il.content_hash,
            il.status,
            il.media_type,
            il.confidence_score,
            il.detected_title,
            il.error_detail,
            il.created_at,
            il.updated_at,
            lj.state AS identity_state,
            COALESCE(pc.total_people, 0) AS total_people,
            COALESCE(pc.enriched_people, 0) AS enriched_people,
            COALESCE(cf.stage3_core_done, 0) AS stage3_core_done,
            COALESCE(cf.stage3_enhancers_done, 0) AS stage3_enhancers_done
        FROM ingestion_log il
        LEFT JOIN latest_jobs lj
            ON lj.entity_id = COALESCE(il.media_asset_id, il.id)
           AND lj.rn = 1
        LEFT JOIN person_counts pc
            ON pc.entity_id = COALESCE(il.media_asset_id, il.id)
        LEFT JOIN canonical_flags cf
            ON cf.entity_id = COALESCE(il.media_asset_id, il.id)
        WHERE il.ingestion_run_id = @batchId
        ORDER BY il.created_at ASC
        LIMIT @limit OFFSET @offset;
        """;
}
