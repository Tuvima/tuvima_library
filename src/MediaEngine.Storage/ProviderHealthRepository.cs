using Dapper;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;
using MediaEngine.Storage.Contracts;
using Microsoft.Extensions.Logging;

namespace MediaEngine.Storage;

/// <summary>
/// SQLite-backed persistence for provider health records.
/// </summary>
public sealed class ProviderHealthRepository : IProviderHealthRepository
{
    private readonly IDatabaseConnection _db;
    private readonly ILogger<ProviderHealthRepository> _logger;

    public ProviderHealthRepository(IDatabaseConnection db, ILogger<ProviderHealthRepository> logger)
    {
        _db = db;
        _logger = logger;
    }

    public Task<ProviderHealthRecord?> GetAsync(string providerId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        var row = conn.QueryFirstOrDefault<ProviderHealthRow>(
            "SELECT * FROM provider_health WHERE provider_id = @ProviderId",
            new { ProviderId = providerId });
        return Task.FromResult(row?.ToRecord());
    }

    public Task<IReadOnlyList<ProviderHealthRecord>> GetAllAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        var rows = conn.Query<ProviderHealthRow>("SELECT * FROM provider_health");
        return Task.FromResult<IReadOnlyList<ProviderHealthRecord>>(rows.Select(r => r.ToRecord()).ToList());
    }

    public Task<IReadOnlyList<ProviderHealthRecord>> GetDownProvidersAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        var rows = conn.Query<ProviderHealthRow>(
            "SELECT * FROM provider_health WHERE status = 'Down'");
        return Task.FromResult<IReadOnlyList<ProviderHealthRecord>>(rows.Select(r => r.ToRecord()).ToList());
    }

    public Task UpsertAsync(ProviderHealthRecord record, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        conn.Execute(@"
            INSERT INTO provider_health (provider_id, status, consecutive_failures,
                last_check_at, last_success_at, last_failure_at, last_failure_reason,
                next_check_at, down_since)
            VALUES (@ProviderId, @Status, @ConsecutiveFailures,
                @LastCheckAt, @LastSuccessAt, @LastFailureAt, @LastFailureReason,
                @NextCheckAt, @DownSince)
            ON CONFLICT(provider_id) DO UPDATE SET
                status = @Status,
                consecutive_failures = @ConsecutiveFailures,
                last_check_at = @LastCheckAt,
                last_success_at = @LastSuccessAt,
                last_failure_at = @LastFailureAt,
                last_failure_reason = @LastFailureReason,
                next_check_at = @NextCheckAt,
                down_since = @DownSince",
            new
            {
                record.ProviderId,
                Status = record.Status.ToString(),
                record.ConsecutiveFailures,
                LastCheckAt = record.LastCheckAt?.ToString("o"),
                LastSuccessAt = record.LastSuccessAt?.ToString("o"),
                LastFailureAt = record.LastFailureAt?.ToString("o"),
                record.LastFailureReason,
                NextCheckAt = record.NextCheckAt?.ToString("o"),
                DownSince = record.DownSince?.ToString("o"),
            });
        return Task.CompletedTask;
    }

    public Task<bool> RecordSuccessAsync(string providerId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var now = DateTimeOffset.UtcNow.ToString("o");
        using var conn = _db.CreateConnection();

        // Check if provider was Down before this success.
        var previousStatus = conn.QueryFirstOrDefault<string>(
            "SELECT status FROM provider_health WHERE provider_id = @ProviderId",
            new { ProviderId = providerId });

        bool wasDown = string.Equals(previousStatus, "Down", StringComparison.OrdinalIgnoreCase);

        conn.Execute(@"
            INSERT INTO provider_health (provider_id, status, consecutive_failures,
                last_check_at, last_success_at, down_since)
            VALUES (@ProviderId, 'Healthy', 0, @Now, @Now, NULL)
            ON CONFLICT(provider_id) DO UPDATE SET
                status = 'Healthy',
                consecutive_failures = 0,
                last_check_at = @Now,
                last_success_at = @Now,
                next_check_at = NULL,
                down_since = NULL",
            new { ProviderId = providerId, Now = now });

        if (wasDown)
            _logger.LogInformation("Provider {Provider} recovered — was down since last failure", providerId);

        return Task.FromResult(wasDown);
    }

    public Task<ProviderHealthStatus> RecordFailureAsync(string providerId, string reason, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var now = DateTimeOffset.UtcNow;
        using var conn = _db.CreateConnection();

        // Get current state.
        var current = conn.QueryFirstOrDefault<ProviderHealthRow>(
            "SELECT * FROM provider_health WHERE provider_id = @ProviderId",
            new { ProviderId = providerId });

        int newFailures = (current?.consecutive_failures ?? 0) + 1;
        var newStatus = newFailures >= 3
            ? ProviderHealthStatus.Down
            : ProviderHealthStatus.Degraded;

        // Compute next probe time with escalating backoff.
        var nextCheck = newStatus == ProviderHealthStatus.Down
            ? ComputeNextProbeTime(now, current?.down_since)
            : now.AddMinutes(5); // Degraded: recheck in 5 min.

        var downSince = newStatus == ProviderHealthStatus.Down
            ? (current?.down_since ?? now.ToString("o"))
            : null;

        conn.Execute(@"
            INSERT INTO provider_health (provider_id, status, consecutive_failures,
                last_check_at, last_failure_at, last_failure_reason, next_check_at, down_since)
            VALUES (@ProviderId, @Status, @Failures, @Now, @Now, @Reason, @NextCheck, @DownSince)
            ON CONFLICT(provider_id) DO UPDATE SET
                status = @Status,
                consecutive_failures = @Failures,
                last_check_at = @Now,
                last_failure_at = @Now,
                last_failure_reason = @Reason,
                next_check_at = @NextCheck,
                down_since = COALESCE(provider_health.down_since, @DownSince)",
            new
            {
                ProviderId = providerId,
                Status = newStatus.ToString(),
                Failures = newFailures,
                Now = now.ToString("o"),
                Reason = reason,
                NextCheck = nextCheck.ToString("o"),
                DownSince = downSince,
            });

        if (newStatus == ProviderHealthStatus.Down && newFailures == 3)
            _logger.LogWarning("Provider {Provider} marked DOWN — {Reason}", providerId, reason);

        return Task.FromResult(newStatus);
    }

    /// <summary>
    /// Escalating backoff: first 30 min → every 5 min, then to 2 hrs → every 15 min, after → every 1 hr.
    /// </summary>
    private static DateTimeOffset ComputeNextProbeTime(DateTimeOffset now, string? downSinceStr)
    {
        if (string.IsNullOrEmpty(downSinceStr) ||
            !DateTimeOffset.TryParse(downSinceStr, out var downSince))
            return now.AddMinutes(5);

        var downDuration = now - downSince;
        if (downDuration.TotalMinutes < 30)
            return now.AddMinutes(5);
        if (downDuration.TotalHours < 2)
            return now.AddMinutes(15);
        return now.AddHours(1);
    }

    // ── Internal Dapper row mapping ──────────────────────────────

    private sealed class ProviderHealthRow
    {
        public string provider_id { get; set; } = "";
        public string status { get; set; } = "Healthy";
        public int consecutive_failures { get; set; }
        public string? last_check_at { get; set; }
        public string? last_success_at { get; set; }
        public string? last_failure_at { get; set; }
        public string? last_failure_reason { get; set; }
        public string? next_check_at { get; set; }
        public string? down_since { get; set; }

        public ProviderHealthRecord ToRecord() => new()
        {
            ProviderId = provider_id,
            Status = Enum.TryParse<ProviderHealthStatus>(status, true, out var s) ? s : ProviderHealthStatus.Healthy,
            ConsecutiveFailures = consecutive_failures,
            LastCheckAt = ParseOffset(last_check_at),
            LastSuccessAt = ParseOffset(last_success_at),
            LastFailureAt = ParseOffset(last_failure_at),
            LastFailureReason = last_failure_reason,
            NextCheckAt = ParseOffset(next_check_at),
            DownSince = ParseOffset(down_since),
        };

        private static DateTimeOffset? ParseOffset(string? s)
            => string.IsNullOrEmpty(s) ? null
                : DateTimeOffset.TryParse(s, out var v) ? v : null;
    }
}
