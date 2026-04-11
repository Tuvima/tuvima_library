using Dapper;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Storage;

/// <summary>
/// SQLite implementation of <see cref="IIdentityJobRepository"/>.
/// Manages durable identity pipeline jobs that survive engine restarts.
/// </summary>
public sealed class IdentityJobRepository : IIdentityJobRepository
{
    private readonly IDatabaseConnection _db;

    public IdentityJobRepository(IDatabaseConnection db) => _db = db;

    public async Task CreateAsync(IdentityJob job, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        await conn.ExecuteAsync("""
            INSERT INTO identity_jobs
                (id, entity_id, entity_type, media_type, ingestion_run_id,
                 state, pass, attempt_count, lease_owner, lease_expires_at,
                 selected_candidate_id, resolved_qid, last_error, next_retry_at,
                 created_at, updated_at)
            VALUES
                (@Id, @EntityId, @EntityType, @MediaType, @IngestionRunId,
                 @State, @Pass, @AttemptCount, @LeaseOwner, @LeaseExpiresAt,
                 @SelectedCandidateId, @ResolvedQid, @LastError, @NextRetryAt,
                 @CreatedAt, @UpdatedAt);
            """,
            new
            {
                Id                  = job.Id.ToString(),
                EntityId            = job.EntityId.ToString(),
                job.EntityType,
                job.MediaType,
                IngestionRunId      = job.IngestionRunId?.ToString(),
                job.State,
                job.Pass,
                job.AttemptCount,
                job.LeaseOwner,
                LeaseExpiresAt      = job.LeaseExpiresAt?.ToString("O"),
                SelectedCandidateId = job.SelectedCandidateId?.ToString(),
                job.ResolvedQid,
                job.LastError,
                NextRetryAt         = job.NextRetryAt?.ToString("O"),
                CreatedAt           = job.CreatedAt.ToString("O"),
                UpdatedAt           = job.UpdatedAt.ToString("O"),
            });
    }

    public async Task<IdentityJob?> GetByEntityAsync(Guid entityId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        var row = await conn.QueryFirstOrDefaultAsync<IdentityJobRow>(SelectSql + " WHERE entity_id = @entityId LIMIT 1;",
            new { entityId = entityId.ToString() });
        return row is null ? null : MapRow(row);
    }

    public async Task<IdentityJob?> GetByIdAsync(Guid jobId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        var row = await conn.QueryFirstOrDefaultAsync<IdentityJobRow>(SelectSql + " WHERE id = @jobId LIMIT 1;",
            new { jobId = jobId.ToString() });
        return row is null ? null : MapRow(row);
    }

    public async Task<IReadOnlyList<IdentityJob>> LeaseNextAsync(
        string workerName,
        IReadOnlyList<IdentityJobState> states,
        int batchSize,
        TimeSpan leaseDuration,
        IReadOnlyList<string>? excludeRunIds = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var now = DateTimeOffset.UtcNow.ToString("O");
        var leaseExpiry = DateTimeOffset.UtcNow.Add(leaseDuration).ToString("O");
        // Build IN clause from enum values (safe — not user input).
        var stateList = string.Join(", ", states.Select(s => $"'{s}'"));

        // Validate all run ID strings are valid GUIDs before interpolating into SQL.
        // Values are written by this codebase but validated defensively.
        var validExcludeRunIds = excludeRunIds?.Where(id => Guid.TryParse(id, out _)).ToList();

        // Build optional exclusion clause. Jobs with NULL ingestion_run_id
        // (ad-hoc / manual) always pass through regardless of the gate.
        var excludeClause = validExcludeRunIds is { Count: > 0 }
            ? $"AND (ingestion_run_id IS NULL OR ingestion_run_id NOT IN ({string.Join(", ", validExcludeRunIds.Select(id => $"'{id}'"))}))"
            : "";

        var sql = $"""
            UPDATE identity_jobs
            SET    lease_owner = @workerName,
                   lease_expires_at = @leaseExpiry,
                   updated_at = @now
            WHERE  id IN (
                SELECT id FROM identity_jobs
                WHERE  state IN ({stateList})
                  AND  (lease_owner IS NULL OR lease_expires_at < @now)
                  AND  (next_retry_at IS NULL OR next_retry_at <= @now)
                  {excludeClause}
                ORDER BY created_at ASC
                LIMIT  @batchSize
            )
            RETURNING id              AS Id,
                      entity_id       AS EntityId,
                      entity_type     AS EntityType,
                      media_type      AS MediaType,
                      ingestion_run_id AS IngestionRunId,
                      state           AS State,
                      pass            AS Pass,
                      attempt_count   AS AttemptCount,
                      lease_owner     AS LeaseOwner,
                      lease_expires_at AS LeaseExpiresAt,
                      selected_candidate_id AS SelectedCandidateId,
                      resolved_qid    AS ResolvedQid,
                      last_error      AS LastError,
                      next_retry_at   AS NextRetryAt,
                      created_at      AS CreatedAt,
                      updated_at      AS UpdatedAt;
            """;

        using var conn = _db.CreateConnection();
        var rows = await conn.QueryAsync<IdentityJobRow>(sql, new { workerName, leaseExpiry, now, batchSize });
        return rows.Select(MapRow).ToList();
    }

    public async Task UpdateStateAsync(Guid jobId, IdentityJobState newState, string? error = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        await conn.ExecuteAsync("""
            UPDATE identity_jobs
            SET    state            = @state,
                   last_error       = @error,
                   lease_owner      = NULL,
                   lease_expires_at = NULL,
                   attempt_count    = attempt_count + 1,
                   updated_at       = @now
            WHERE  id = @jobId;
            """,
            new
            {
                jobId = jobId.ToString(),
                state = newState.ToString(),
                error,
                now   = DateTimeOffset.UtcNow.ToString("O"),
            });
    }

    public async Task SetSelectedCandidateAsync(Guid jobId, Guid candidateId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        await conn.ExecuteAsync("""
            UPDATE identity_jobs
            SET    selected_candidate_id = @candidateId,
                   updated_at           = @now
            WHERE  id = @jobId;
            """,
            new
            {
                jobId       = jobId.ToString(),
                candidateId = candidateId.ToString(),
                now         = DateTimeOffset.UtcNow.ToString("O"),
            });
    }

    public async Task SetResolvedQidAsync(Guid jobId, string qid, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        await conn.ExecuteAsync("""
            UPDATE identity_jobs
            SET    resolved_qid = @qid,
                   updated_at   = @now
            WHERE  id = @jobId;
            """,
            new
            {
                jobId = jobId.ToString(),
                qid,
                now   = DateTimeOffset.UtcNow.ToString("O"),
            });
    }

    public async Task<IReadOnlyList<IdentityJob>> GetStaleAsync(TimeSpan age, int limit, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var cutoff = DateTimeOffset.UtcNow.Subtract(age).ToString("O");
        using var conn = _db.CreateConnection();
        var rows = await conn.QueryAsync<IdentityJobRow>(
            SelectSql + """
                 WHERE state NOT IN ('Completed', 'Failed', 'RetailNoMatch')
                   AND updated_at < @cutoff
                 ORDER BY updated_at ASC
                 LIMIT @limit;
                """,
            new { cutoff, limit });
        return rows.Select(MapRow).ToList();
    }

    public async Task<int> ReclaimStuckJobsAsync(TimeSpan stuckThreshold, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var cutoff = DateTimeOffset.UtcNow.Subtract(stuckThreshold).ToString("O");
        var now = DateTimeOffset.UtcNow.ToString("O");
        using var conn = _db.CreateConnection();
        return await conn.ExecuteAsync("""
            UPDATE identity_jobs
            SET    state = CASE state
                       WHEN 'RetailSearching' THEN 'Queued'
                       WHEN 'BridgeSearching' THEN 'RetailMatched'
                       WHEN 'Hydrating'       THEN 'QidResolved'
                   END,
                   lease_owner      = NULL,
                   lease_expires_at = NULL,
                   last_error       = 'Reclaimed from stuck intermediate state',
                   updated_at       = @now
            WHERE  state IN ('RetailSearching', 'BridgeSearching', 'Hydrating')
              AND  lease_owner IS NULL
              AND  updated_at < @cutoff
              AND  attempt_count < 5;
            """,
            new { cutoff, now });
    }

    public async Task<IReadOnlyList<IdentityJob>> GetByStateAsync(IdentityJobState state, int limit, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        var rows = await conn.QueryAsync<IdentityJobRow>(
            SelectSql + " WHERE state = @state ORDER BY created_at ASC LIMIT @limit;",
            new { state = state.ToString(), limit });
        return rows.Select(MapRow).ToList();
    }

    public async Task<IReadOnlyDictionary<string, int>> GetStateCountsByRunAsync(Guid ingestionRunId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        var rows = await conn.QueryAsync("""
            SELECT state, COUNT(*) AS cnt
            FROM   identity_jobs
            WHERE  ingestion_run_id = @runId
            GROUP BY state;
            """,
            new { runId = ingestionRunId.ToString() });
        return rows.ToDictionary(
            r => (string)r.state,
            r => (int)r.cnt);
    }

    public async Task<int> CountActiveAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        return await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM identity_jobs WHERE state NOT IN ('Completed', 'Failed')");
    }

    public async Task<IReadOnlyDictionary<string, int>> GetPendingStage1CountsByRunAsync(
        IReadOnlyList<string> ingestionRunIds, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (ingestionRunIds.Count == 0)
            return new Dictionary<string, int>();

        // Validate all run ID strings are valid GUIDs before interpolating into SQL.
        var validRunIds = ingestionRunIds.Where(id => Guid.TryParse(id, out _)).ToList();
        if (validRunIds.Count == 0)
            return new Dictionary<string, int>();

        // Build IN clause from validated GUID strings.
        var idList = string.Join(", ", validRunIds.Select(id => $"'{id}'"));

        var sql = $"""
            SELECT ingestion_run_id, COUNT(*) AS cnt
            FROM   identity_jobs
            WHERE  ingestion_run_id IN ({idList})
              AND  state IN ('Queued', 'RetailSearching')
            GROUP BY ingestion_run_id;
            """;

        using var conn = _db.CreateConnection();
        var rows = await conn.QueryAsync(sql);
        return rows.ToDictionary(
            r => (string)r.ingestion_run_id,
            r => (int)r.cnt);
    }

    public async Task ReleaseLeaseAsync(Guid jobId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        await conn.ExecuteAsync("""
            UPDATE identity_jobs
            SET    lease_owner      = NULL,
                   lease_expires_at = NULL,
                   updated_at       = @now
            WHERE  id = @jobId;
            """,
            new
            {
                jobId = jobId.ToString(),
                now   = DateTimeOffset.UtcNow.ToString("O"),
            });
    }

    // ── Shared SELECT prefix ─────────────────────────────────────────────────

    private const string SelectSql = """
        SELECT id               AS Id,
               entity_id        AS EntityId,
               entity_type      AS EntityType,
               media_type       AS MediaType,
               ingestion_run_id AS IngestionRunId,
               state            AS State,
               pass             AS Pass,
               attempt_count    AS AttemptCount,
               lease_owner      AS LeaseOwner,
               lease_expires_at AS LeaseExpiresAt,
               selected_candidate_id AS SelectedCandidateId,
               resolved_qid     AS ResolvedQid,
               last_error       AS LastError,
               next_retry_at    AS NextRetryAt,
               created_at       AS CreatedAt,
               updated_at       AS UpdatedAt
        FROM   identity_jobs
        """;

    // ── Private intermediate row type and mapper ─────────────────────────────

    private sealed class IdentityJobRow
    {
        public string  Id                  { get; set; } = "";
        public string  EntityId            { get; set; } = "";
        public string  EntityType          { get; set; } = "";
        public string  MediaType           { get; set; } = "";
        public string? IngestionRunId      { get; set; }
        public string  State               { get; set; } = "";
        public string  Pass                { get; set; } = "";
        public int     AttemptCount        { get; set; }
        public string? LeaseOwner          { get; set; }
        public string? LeaseExpiresAt      { get; set; }
        public string? SelectedCandidateId { get; set; }
        public string? ResolvedQid         { get; set; }
        public string? LastError           { get; set; }
        public string? NextRetryAt         { get; set; }
        public string  CreatedAt           { get; set; } = "";
        public string  UpdatedAt           { get; set; } = "";
    }

    private static IdentityJob MapRow(IdentityJobRow r) => new()
    {
        Id                  = Guid.Parse(r.Id),
        EntityId            = Guid.Parse(r.EntityId),
        EntityType          = r.EntityType,
        MediaType           = r.MediaType,
        IngestionRunId      = r.IngestionRunId is not null ? Guid.Parse(r.IngestionRunId) : null,
        State               = r.State,
        Pass                = r.Pass,
        AttemptCount        = r.AttemptCount,
        LeaseOwner          = r.LeaseOwner,
        LeaseExpiresAt      = r.LeaseExpiresAt is not null ? DateTimeOffset.Parse(r.LeaseExpiresAt) : null,
        SelectedCandidateId = r.SelectedCandidateId is not null ? Guid.Parse(r.SelectedCandidateId) : null,
        ResolvedQid         = r.ResolvedQid,
        LastError           = r.LastError,
        NextRetryAt         = r.NextRetryAt is not null ? DateTimeOffset.Parse(r.NextRetryAt) : null,
        CreatedAt           = DateTimeOffset.Parse(r.CreatedAt),
        UpdatedAt           = DateTimeOffset.Parse(r.UpdatedAt),
    };
}
