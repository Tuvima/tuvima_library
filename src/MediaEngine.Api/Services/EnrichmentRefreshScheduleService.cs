using Dapper;
using MediaEngine.Contracts.Operations;
using MediaEngine.Domain.Configuration;
using MediaEngine.Domain.Constants;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Models;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Api.Services;

/// <summary>
/// Owns the durable calendar for recurring enrichment. Calendar rows describe
/// future intent; actual ingestion work is created only when an item is due or
/// explicitly started by a curator.
/// </summary>
public sealed class EnrichmentRefreshScheduleService
{
    private readonly IDatabaseConnection _db;
    private readonly IConfigurationLoader _configuration;
    private readonly IPersonRepository _persons;
    private readonly ICanonicalValueRepository _canonicals;
    private readonly IMetadataHarvestingService _harvesting;
    private readonly IHydrationPipelineService _hydration;
    private readonly ISystemActivityRepository _activity;

    public EnrichmentRefreshScheduleService(
        IDatabaseConnection db,
        IConfigurationLoader configuration,
        IPersonRepository persons,
        ICanonicalValueRepository canonicals,
        IMetadataHarvestingService harvesting,
        IHydrationPipelineService hydration,
        ISystemActivityRepository activity)
    {
        _db = db;
        _configuration = configuration;
        _persons = persons;
        _canonicals = canonicals;
        _harvesting = harvesting;
        _hydration = hydration;
        _activity = activity;
    }

    public Task<EnrichmentRefreshScheduleResponse> GetAsync(
        string? entityType,
        string? status,
        int limit,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        Synchronize();

        using var conn = _db.CreateConnection();
        var rows = conn.Query<ScheduleRow>(
            """
            SELECT schedule.entity_type AS EntityType,
                   schedule.entity_id AS EntityId,
                   COALESCE(person.name, title.value, schedule.entity_type) AS EntityName,
                   schedule.stage AS Stage,
                   schedule.provider_id AS ProviderId,
                   schedule.policy_key AS PolicyKey,
                   schedule.interval_days AS IntervalDays,
                   schedule.last_success_at AS LastSuccessAt,
                   schedule.last_attempt_at AS LastAttemptAt,
                   schedule.next_due_at AS NextDueAt,
                   schedule.status AS Status,
                   schedule.failure_count AS FailureCount,
                   schedule.retry_after AS RetryAfter,
                   schedule.operation_id AS OperationId,
                   schedule.reason AS Reason
            FROM enrichment_refresh_schedule schedule
            LEFT JOIN persons person
                ON schedule.entity_type = 'Person' AND person.id = schedule.entity_id
            LEFT JOIN canonical_values title
                ON title.entity_id = schedule.entity_id AND title.key = 'title'
            WHERE (@entityType IS NULL OR schedule.entity_type = @entityType COLLATE NOCASE)
              AND (@status IS NULL OR schedule.status = @status COLLATE NOCASE)
            ORDER BY schedule.next_due_at, EntityName COLLATE NOCASE
            LIMIT @limit;
            """,
            new
            {
                entityType = string.IsNullOrWhiteSpace(entityType) ? null : entityType.Trim(),
                status = string.IsNullOrWhiteSpace(status) ? null : status.Trim(),
                limit = Math.Clamp(limit, 1, 1000),
            }).Select(Map).ToList();

        var now = DateTimeOffset.UtcNow;
        var summary = conn.QuerySingle<ScheduleSummaryRow>(
            """
            SELECT COUNT(*) AS TotalCount,
                   SUM(CASE WHEN next_due_at <= @now THEN 1 ELSE 0 END) AS OverdueCount,
                   SUM(CASE WHEN next_due_at > @now AND next_due_at <= @sevenDays THEN 1 ELSE 0 END) AS DueNextSevenDaysCount
            FROM enrichment_refresh_schedule
            WHERE (@entityType IS NULL OR entity_type = @entityType COLLATE NOCASE)
              AND (@status IS NULL OR status = @status COLLATE NOCASE);
            """,
            new
            {
                entityType = string.IsNullOrWhiteSpace(entityType) ? null : entityType.Trim(),
                status = string.IsNullOrWhiteSpace(status) ? null : status.Trim(),
                now = now.ToString("O"),
                sevenDays = now.AddDays(7).ToString("O"),
            });
        return Task.FromResult(new EnrichmentRefreshScheduleResponse
        {
            Items = rows,
            TotalCount = summary.TotalCount,
            OverdueCount = summary.OverdueCount,
            DueNextSevenDaysCount = summary.DueNextSevenDaysCount,
            GeneratedAt = now,
        });
    }

    public async Task<EnrichmentRefreshQueuedResponse?> QueueNowAsync(
        string entityType,
        Guid entityId,
        string reason,
        CancellationToken ct = default)
    {
        Synchronize();
        var normalizedType = entityType.Trim();
        var now = DateTimeOffset.UtcNow;
        Guid? operationId = null;

        if (normalizedType.Equals("Person", StringComparison.OrdinalIgnoreCase))
        {
            var person = await _persons.FindByIdAsync(entityId, ct).ConfigureAwait(false);
            if (person is null)
                return null;

            await _harvesting.EnqueueAsync(new HarvestRequest
            {
                EntityId = person.Id,
                EntityType = EntityType.Person,
                MediaType = MediaType.Unknown,
                Hints = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["name"] = person.Name,
                    ["wikidata_qid"] = person.WikidataQid ?? string.Empty,
                    ["role"] = person.IsGroup ? "Artist" : string.Join(", ", person.Roles),
                },
                PreResolvedQid = person.WikidataQid,
                IsUserResolution = reason.Equals("Manual", StringComparison.OrdinalIgnoreCase),
                SuppressReviewCreation = false,
            }, ct).ConfigureAwait(false);
        }
        else
        {
            using var conn = _db.CreateConnection();
            var target = conn.QueryFirstOrDefault<AssetRefreshRow>(
                """
                SELECT work.id AS EntityId,
                       work.media_type AS MediaType,
                       'Work' AS EntityType
                FROM works work
                WHERE work.id = @entityId

                UNION ALL

                SELECT asset.id AS EntityId,
                       work.media_type AS MediaType,
                       'MediaAsset' AS EntityType
                FROM media_assets asset
                INNER JOIN editions edition ON edition.id = asset.edition_id
                INNER JOIN works work ON work.id = edition.work_id
                WHERE asset.id = @entityId
                LIMIT 1;
                """,
                new { entityId });
            if (target is null)
                return null;

            var canonicalRows = await _canonicals.GetByEntityAsync(entityId, ct).ConfigureAwait(false);
            var hints = canonicalRows.ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase);
            hints.TryGetValue("wikidata_qid", out var qid);
            _ = Enum.TryParse<MediaType>(target.MediaType, true, out var mediaType);
            operationId = await _hydration.EnqueueAsync(new HarvestRequest
            {
                EntityId = entityId,
                EntityType = target.EntityType == "Work" ? EntityType.Work : EntityType.MediaAsset,
                MediaType = mediaType,
                Hints = hints,
                PreResolvedQid = qid,
                SkipRetailStage = !string.IsNullOrWhiteSpace(qid),
                IsUserResolution = reason.Equals("Manual", StringComparison.OrdinalIgnoreCase),
                Pass = HydrationPass.Universe,
            }, ct).ConfigureAwait(false);
        }

        await MarkQueuedAsync(normalizedType, entityId, operationId, reason, now, ct).ConfigureAwait(false);
        await _activity.LogAsync(new SystemActivityEntry
        {
            ActionType = SystemActionType.HydrationEnqueued,
            EntityId = entityId,
            EntityType = normalizedType,
            Detail = $"{(reason.Equals("Manual", StringComparison.OrdinalIgnoreCase) ? "Manual" : "Scheduled")} enrichment refresh queued.",
            ChangesJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                reason,
                operation_id = operationId,
                full_enrichment_cycle = true,
            }),
            OccurredAt = now,
        }, ct).ConfigureAwait(false);

        return new EnrichmentRefreshQueuedResponse
        {
            EntityType = normalizedType,
            EntityId = entityId,
            OperationId = operationId,
            Status = "Queued",
            Message = "The item was queued for the full enrichment cycle.",
        };
    }

    public async Task<int> QueueDueAsync(int maxItems, CancellationToken ct = default)
    {
        var due = await GetAsync(null, "Scheduled", Math.Clamp(maxItems, 1, 100), ct).ConfigureAwait(false);
        var queued = 0;
        foreach (var item in due.Items.Where(item => item.NextDueAt <= DateTimeOffset.UtcNow))
        {
            if (await QueueNowAsync(item.EntityType, item.EntityId, "Scheduled", ct).ConfigureAwait(false) is not null)
                queued++;
        }

        return queued;
    }

    private void Synchronize()
    {
        var settings = _configuration.LoadHydration();
        var personDays = Math.Max(1, settings.PersonRefreshDays);
        var mediaDays = Math.Max(1, settings.Stage3RefreshDays);
        var now = DateTimeOffset.UtcNow;

        using var conn = _db.CreateConnection();
        using var tx = conn.BeginTransaction();
        var people = conn.Query<SeedRow>(
            """
            SELECT id AS EntityId, enriched_at AS LastSuccessAt, created_at AS CreatedAt
            FROM persons
            WHERE wikidata_qid IS NOT NULL AND TRIM(wikidata_qid) <> '';
            """, transaction: tx);
        foreach (var person in people)
            UpsertSeed(conn, tx, "Person", person, "people", "wikidata", "people.default", personDays, now);

        var works = conn.Query<SeedRow>(
            """
            SELECT work.id AS EntityId,
                   enriched.value AS LastSuccessAt,
                   @now AS CreatedAt
            FROM works work
            INNER JOIN canonical_values qid
                ON qid.entity_id = work.id AND qid.key = 'wikidata_qid'
            LEFT JOIN canonical_values enriched
                ON enriched.entity_id = work.id AND enriched.key = 'stage3_enriched_at'
            WHERE TRIM(qid.value) <> ''
              AND work.is_catalog_only = 0;
            """, new { now = now.ToString("O") }, tx);
        foreach (var work in works)
            UpsertSeed(conn, tx, "Work", work, "universe", "wikidata", "media.stage3", mediaDays, now);

        conn.Execute(
            """
            UPDATE enrichment_refresh_schedule
            SET status = 'Scheduled',
                failure_count = failure_count + 1,
                retry_after = @now,
                next_due_at = @now,
                reason = 'Queue completion was not observed; eligible for retry',
                updated_at = @now
            WHERE status = 'Queued'
              AND last_attempt_at < @staleAttempt
              AND (last_success_at IS NULL OR last_success_at < last_attempt_at);
            """,
            new
            {
                now = now.ToString("O"),
                staleAttempt = now.AddHours(-24).ToString("O"),
            }, tx);

        tx.Commit();
    }

    private static void UpsertSeed(
        System.Data.IDbConnection conn,
        System.Data.IDbTransaction tx,
        string entityType,
        SeedRow seed,
        string stage,
        string provider,
        string policy,
        int intervalDays,
        DateTimeOffset now)
    {
        var lastSuccess = Parse(seed.LastSuccessAt);
        var nextDue = lastSuccess.HasValue
            ? lastSuccess.Value.AddDays(intervalDays).AddHours(StableJitterHours(seed.EntityId))
            : now;
        conn.Execute(
            """
            INSERT INTO enrichment_refresh_schedule
                (entity_type, entity_id, stage, provider_id, policy_key, interval_days,
                 last_success_at, next_due_at, status, updated_at)
            VALUES
                (@entityType, @entityId, @stage, @provider, @policy, @intervalDays,
                 @lastSuccess, @nextDue, 'Scheduled', @updatedAt)
            ON CONFLICT(entity_type, entity_id, stage, provider_id) DO UPDATE SET
                policy_key = excluded.policy_key,
                interval_days = excluded.interval_days,
                last_success_at = CASE
                    WHEN excluded.last_success_at IS NOT NULL
                         AND (enrichment_refresh_schedule.last_success_at IS NULL
                              OR excluded.last_success_at > enrichment_refresh_schedule.last_success_at)
                    THEN excluded.last_success_at
                    ELSE enrichment_refresh_schedule.last_success_at
                END,
                next_due_at = CASE
                    WHEN excluded.last_success_at IS NOT NULL
                         AND (enrichment_refresh_schedule.last_success_at IS NULL
                              OR excluded.last_success_at > enrichment_refresh_schedule.last_success_at)
                    THEN excluded.next_due_at
                    ELSE enrichment_refresh_schedule.next_due_at
                END,
                status = CASE
                    WHEN excluded.last_success_at IS NOT NULL
                         AND enrichment_refresh_schedule.last_attempt_at IS NOT NULL
                         AND excluded.last_success_at >= enrichment_refresh_schedule.last_attempt_at
                    THEN 'Scheduled'
                    ELSE enrichment_refresh_schedule.status
                END,
                updated_at = excluded.updated_at;
            """,
            new
            {
                entityType,
                entityId = seed.EntityId,
                stage,
                provider,
                policy,
                intervalDays,
                lastSuccess = lastSuccess?.ToString("O"),
                nextDue = nextDue.ToString("O"),
                updatedAt = now.ToString("O"),
            }, tx);
    }

    private Task MarkQueuedAsync(
        string entityType,
        Guid entityId,
        Guid? operationId,
        string reason,
        DateTimeOffset now,
        CancellationToken ct)
        => _db.ExecuteWriteAsync((conn, tx, innerCt) => conn.Execute(
            """
            UPDATE enrichment_refresh_schedule
            SET status = 'Queued', last_attempt_at = @now, operation_id = @operationId,
                reason = @reason, updated_at = @now
            WHERE entity_type = @entityType COLLATE NOCASE AND entity_id = @entityId;
            """,
            new { entityType, entityId, operationId, reason, now = now.ToString("O") }, tx), ct);

    private static EnrichmentRefreshScheduleDto Map(ScheduleRow row) => new()
    {
        EntityType = row.EntityType,
        EntityId = row.EntityId,
        EntityName = row.EntityName,
        Stage = row.Stage,
        ProviderId = row.ProviderId,
        PolicyKey = row.PolicyKey,
        IntervalDays = row.IntervalDays,
        LastSuccessAt = Parse(row.LastSuccessAt),
        LastAttemptAt = Parse(row.LastAttemptAt),
        NextDueAt = Parse(row.NextDueAt) ?? DateTimeOffset.UtcNow,
        Status = row.Status,
        FailureCount = row.FailureCount,
        RetryAfter = Parse(row.RetryAfter),
        OperationId = row.OperationId,
        Reason = row.Reason,
    };

    private static DateTimeOffset? Parse(string? value)
        => DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;

    private static int StableJitterHours(Guid entityId) => entityId.ToByteArray()[0] % 24;

    private sealed class SeedRow
    {
        public Guid EntityId { get; init; }
        public string? LastSuccessAt { get; init; }
        public string? CreatedAt { get; init; }
    }

    private sealed class AssetRefreshRow
    {
        public Guid EntityId { get; init; }
        public string? MediaType { get; init; }
        public string EntityType { get; init; } = "MediaAsset";
    }

    private sealed class ScheduleRow
    {
        public string EntityType { get; init; } = string.Empty;
        public Guid EntityId { get; init; }
        public string EntityName { get; init; } = string.Empty;
        public string Stage { get; init; } = string.Empty;
        public string ProviderId { get; init; } = string.Empty;
        public string PolicyKey { get; init; } = string.Empty;
        public int IntervalDays { get; init; }
        public string? LastSuccessAt { get; init; }
        public string? LastAttemptAt { get; init; }
        public string NextDueAt { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public int FailureCount { get; init; }
        public string? RetryAfter { get; init; }
        public Guid? OperationId { get; init; }
        public string? Reason { get; init; }
    }

    private sealed class ScheduleSummaryRow
    {
        public int TotalCount { get; init; }
        public int OverdueCount { get; init; }
        public int DueNextSevenDaysCount { get; init; }
    }
}
