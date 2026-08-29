using Dapper;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Storage;

public sealed record OnboardingWorkflowRecord(
    int WorkflowVersion,
    string State,
    string CurrentStep,
    Guid? AdministratorProfileId,
    long Revision,
    DateTimeOffset? ClaimedAt,
    DateTimeOffset? CompletedAt,
    IReadOnlyList<OnboardingStepRecord> Steps);

public sealed record OnboardingStepRecord(
    string Key,
    string Status,
    string? Detail,
    string? RepairTarget,
    DateTimeOffset? CompletedAt);

public sealed class OnboardingRepository(IDatabaseConnection database)
{
    public const int CurrentVersion = 1;

    public OnboardingWorkflowRecord Get()
    {
        using var connection = database.CreateConnection();
        var workflow = connection.QuerySingle<WorkflowRow>("""
            SELECT workflow_version AS WorkflowVersion, state AS State,
                   current_step AS CurrentStep, administrator_profile_id AS AdministratorProfileId,
                   revision AS Revision, claimed_at AS ClaimedAt, completed_at AS CompletedAt
            FROM onboarding_workflows WHERE workflow_version = @version;
            """, new { version = CurrentVersion });
        var steps = connection.Query<StepRow>("""
            SELECT step_key AS StepKey, status AS Status, detail AS Detail,
                   repair_target AS RepairTarget, completed_at AS CompletedAt
            FROM onboarding_steps WHERE workflow_version = @version
            ORDER BY CASE step_key
                WHEN 'claim' THEN 1 WHEN 'preflight' THEN 2 WHEN 'administrator' THEN 3
                WHEN 'media-locations' THEN 4 WHEN 'providers' THEN 5 WHEN 'local-ai' THEN 6
                WHEN 'access' THEN 7 WHEN 'readiness' THEN 8 ELSE 99 END;
            """, new { version = CurrentVersion }).AsList();
        return new OnboardingWorkflowRecord(
            workflow.WorkflowVersion, workflow.State, workflow.CurrentStep,
            workflow.AdministratorProfileId, workflow.Revision,
            Parse(workflow.ClaimedAt), Parse(workflow.CompletedAt),
            steps.Select(row => new OnboardingStepRecord(
                row.StepKey, row.Status, row.Detail, row.RepairTarget, Parse(row.CompletedAt))).ToList());
    }

    public async Task<bool> TryClaimAsync(string sessionTokenHash, Guid sessionId, DateTimeOffset expiresAt, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow.ToString("O");
        return await database.ExecuteWriteAsync((connection, transaction, _) =>
        {
            var changed = connection.Execute("""
                UPDATE onboarding_workflows
                SET state = 'claimed', current_step = 'preflight', claimed_at = @now,
                    updated_at = @now, revision = revision + 1
                WHERE workflow_version = @version AND state = 'unclaimed';
                """, new { now, version = CurrentVersion }, transaction);
            if (changed == 0) return false;
            connection.Execute("""
                UPDATE onboarding_steps
                SET status = 'passed', detail = 'Server claimed by the local operator.',
                    completed_at = @now, updated_at = @now
                WHERE workflow_version = @version AND step_key = 'claim';
                """, new { now, version = CurrentVersion }, transaction);
            connection.Execute("""
                INSERT INTO onboarding_sessions
                    (id, workflow_version, token_hash, created_at, expires_at, last_used_at)
                VALUES (@sessionId, @version, @tokenHash, @now, @expiresAt, @now);
                """, new
            {
                sessionId, version = CurrentVersion, tokenHash = sessionTokenHash,
                now, expiresAt = expiresAt.ToString("O"),
            }, transaction);
            return true;
        }, ct).ConfigureAwait(false);
    }

    public async Task<bool> ValidateSessionAsync(string tokenHash, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        using var connection = database.CreateConnection();
        var row = connection.QuerySingleOrDefault<SessionRow>("""
            SELECT id AS Id, expires_at AS ExpiresAt
            FROM onboarding_sessions
            WHERE workflow_version = @version AND token_hash = @tokenHash AND revoked_at IS NULL
            LIMIT 1;
            """, new { version = CurrentVersion, tokenHash });
        if (row is null || !DateTimeOffset.TryParse(row.ExpiresAt, out var expiresAt) || expiresAt <= now)
            return false;
        await database.ExecuteWriteAsync((write, transaction, _) =>
        {
            write.Execute("UPDATE onboarding_sessions SET last_used_at = @now WHERE id = @id;",
                new { now = now.ToString("O"), id = row.Id }, transaction);
        }, ct).ConfigureAwait(false);
        return true;
    }

    public async Task SetStepAsync(
        string stepKey, string status, string? detail, string? repairTarget,
        Guid? administratorProfileId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow.ToString("O");
        await database.ExecuteWriteAsync((connection, transaction, _) =>
        {
            connection.Execute("""
                UPDATE onboarding_steps
                SET status = @status, detail = @detail, repair_target = @repairTarget,
                    completed_at = CASE WHEN @status IN ('passed','deferred') THEN @now ELSE NULL END,
                    updated_at = @now
                WHERE workflow_version = @version AND step_key = @stepKey;
                """, new { status, detail, repairTarget, now, version = CurrentVersion, stepKey }, transaction);
            var next = NextIncomplete(connection, transaction);
            connection.Execute("""
                UPDATE onboarding_workflows
                SET state = CASE WHEN state = 'complete' THEN state ELSE 'in_progress' END,
                    current_step = @next,
                    administrator_profile_id = COALESCE(@administratorProfileId, administrator_profile_id),
                    updated_at = @now, revision = revision + 1
                WHERE workflow_version = @version;
                """, new { next, administratorProfileId, now, version = CurrentVersion }, transaction);
        }, ct).ConfigureAwait(false);
    }

    public async Task<bool> CompleteAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow.ToString("O");
        return await database.ExecuteWriteAsync((connection, transaction, _) =>
        {
            var blocking = connection.ExecuteScalar<int>("""
                SELECT COUNT(*) FROM onboarding_steps
                WHERE workflow_version = @version
                  AND step_key <> 'readiness'
                  AND (
                    (step_key IN ('claim','preflight','administrator','media-locations') AND status <> 'passed')
                    OR
                    (step_key IN ('providers','local-ai','access') AND status NOT IN ('passed','deferred'))
                  );
                """, new { version = CurrentVersion }, transaction);
            if (blocking > 0) return false;
            connection.Execute("""
                UPDATE onboarding_steps SET status = 'passed', detail = 'Setup readiness accepted.',
                    completed_at = @now, updated_at = @now
                WHERE workflow_version = @version AND step_key = 'readiness';
                UPDATE onboarding_workflows SET state = 'complete', current_step = 'readiness',
                    completed_at = @now, updated_at = @now, revision = revision + 1
                WHERE workflow_version = @version;
                UPDATE onboarding_sessions SET revoked_at = @now
                WHERE workflow_version = @version AND revoked_at IS NULL;
                """, new { now, version = CurrentVersion }, transaction);
            return true;
        }, ct).ConfigureAwait(false);
    }

    public async Task ResetAbandonedClaimAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        await database.ExecuteWriteAsync((connection, transaction, _) =>
        {
            var active = connection.ExecuteScalar<int>("""
                SELECT COUNT(*) FROM onboarding_sessions
                WHERE workflow_version = @version AND revoked_at IS NULL AND expires_at > @now;
                """, new { version = CurrentVersion, now = now.ToString("O") }, transaction);
            if (active > 0) return;
            connection.Execute("""
                UPDATE onboarding_workflows SET state = 'unclaimed', current_step = 'claim',
                    claimed_at = NULL, updated_at = @now, revision = revision + 1
                WHERE workflow_version = @version AND state IN ('claimed', 'in_progress');
                UPDATE onboarding_steps SET status = 'not_started', detail = NULL,
                    completed_at = NULL, updated_at = @now
                WHERE workflow_version = @version AND step_key = 'claim';
                """, new { version = CurrentVersion, now = now.ToString("O") }, transaction);
        }, ct).ConfigureAwait(false);
    }

    public void SaveRestoreOperation(Guid id, string archivePath, string originalName, string manifestVersion, string databaseEpoch, string summaryJson)
    {
        using var connection = database.CreateConnection();
        var now = DateTimeOffset.UtcNow.ToString("O");
        connection.Execute("""
            INSERT INTO onboarding_restore_operations
                (id, workflow_version, archive_path, original_file_name, status, manifest_version,
                 database_epoch, summary_json, created_at, updated_at)
            VALUES (@id, @version, @archivePath, @originalName, 'inspected', @manifestVersion,
                    @databaseEpoch, @summaryJson, @now, @now);
            """, new { id, version = CurrentVersion, archivePath, originalName, manifestVersion, databaseEpoch, summaryJson, now });
    }

    public (string ArchivePath, string Status)? GetRestoreOperation(Guid id)
    {
        using var connection = database.CreateConnection();
        var row = connection.QuerySingleOrDefault<RestoreRow>("""
            SELECT archive_path AS ArchivePath, status AS Status
            FROM onboarding_restore_operations WHERE id = @id AND workflow_version = @version;
            """, new { id, version = CurrentVersion });
        return row is null ? null : (row.ArchivePath, row.Status);
    }

    public void MarkRestoreScheduled(Guid id)
    {
        using var connection = database.CreateConnection();
        connection.Execute("""
            UPDATE onboarding_restore_operations SET status = 'scheduled', updated_at = @now
            WHERE id = @id AND workflow_version = @version AND status = 'inspected';
            """, new { id, version = CurrentVersion, now = DateTimeOffset.UtcNow.ToString("O") });
    }

    private static string NextIncomplete(Microsoft.Data.Sqlite.SqliteConnection connection, Microsoft.Data.Sqlite.SqliteTransaction transaction) =>
        connection.QuerySingleOrDefault<string>("""
            SELECT step_key FROM onboarding_steps
            WHERE workflow_version = @version AND status NOT IN ('passed','deferred')
            ORDER BY CASE step_key
                WHEN 'claim' THEN 1 WHEN 'preflight' THEN 2 WHEN 'administrator' THEN 3
                WHEN 'media-locations' THEN 4 WHEN 'providers' THEN 5 WHEN 'local-ai' THEN 6
                WHEN 'access' THEN 7 WHEN 'readiness' THEN 8 ELSE 99 END LIMIT 1;
            """, new { version = CurrentVersion }, transaction) ?? "readiness";

    private static DateTimeOffset? Parse(string? value) =>
        DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;

    private sealed class WorkflowRow
    {
        public int WorkflowVersion { get; init; }
        public string State { get; init; } = "unclaimed";
        public string CurrentStep { get; init; } = "claim";
        public Guid? AdministratorProfileId { get; init; }
        public long Revision { get; init; }
        public string? ClaimedAt { get; init; }
        public string? CompletedAt { get; init; }
    }
    private sealed class StepRow
    {
        public string StepKey { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public string? Detail { get; init; }
        public string? RepairTarget { get; init; }
        public string? CompletedAt { get; init; }
    }
    private sealed class SessionRow { public Guid Id { get; init; } public string ExpiresAt { get; init; } = string.Empty; }
    private sealed class RestoreRow { public string ArchivePath { get; init; } = string.Empty; public string Status { get; init; } = string.Empty; }
}
