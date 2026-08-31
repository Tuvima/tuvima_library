using System.Globalization;
using Dapper;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Storage.Playback;

public sealed class AdaptiveHlsPackageRepository(IDatabaseConnection database)
{
    public Task<AdaptiveHlsPackageRecord?> FindAsync(
        Guid assetId,
        string sourceHash,
        string profileKey,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var connection = database.CreateConnection();
        return Task.FromResult(connection.QueryFirstOrDefault<AdaptiveHlsPackageRecord>("""
            SELECT id AS Id, asset_id AS AssetId, source_hash AS SourceHash,
                   profile_key AS ProfileKey, status AS Status, root_path AS RootPath,
                   total_bytes AS TotalBytes, created_at AS CreatedAt,
                   last_accessed AS LastAccessed, completed_at AS CompletedAt,
                   last_error AS LastError
            FROM adaptive_hls_packages
            WHERE asset_id = @assetId AND source_hash = @sourceHash AND profile_key = @profileKey
            LIMIT 1;
            """, new { assetId, sourceHash, profileKey }));
    }

    public Task<AdaptiveHlsPackageRecord?> FindByIdAsync(Guid id, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var connection = database.CreateConnection();
        return Task.FromResult(connection.QueryFirstOrDefault<AdaptiveHlsPackageRecord>("""
            SELECT id AS Id, asset_id AS AssetId, source_hash AS SourceHash,
                   profile_key AS ProfileKey, status AS Status, root_path AS RootPath,
                   total_bytes AS TotalBytes, created_at AS CreatedAt,
                   last_accessed AS LastAccessed, completed_at AS CompletedAt,
                   last_error AS LastError
            FROM adaptive_hls_packages WHERE id = @id LIMIT 1;
            """, new { id }));
    }

    public async Task<AdaptiveHlsPackageRecord> GetOrCreateAsync(
        Guid assetId,
        string sourceHash,
        string profileKey,
        string rootPath,
        CancellationToken ct = default)
    {
        var existing = await FindAsync(assetId, sourceHash, profileKey, ct).ConfigureAwait(false);
        if (existing is not null) return existing;

        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        using var connection = database.CreateConnection();
        connection.Execute("""
            INSERT OR IGNORE INTO adaptive_hls_packages
                (id, asset_id, source_hash, profile_key, status, root_path, created_at, last_accessed)
            VALUES (@id, @assetId, @sourceHash, @profileKey, 'preparing', @rootPath, @now, @now);
            """, new { id, assetId, sourceHash, profileKey, rootPath, now });
        return await FindAsync(assetId, sourceHash, profileKey, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The adaptive HLS package could not be created.");
    }

    public Task MarkReadyAsync(Guid id, string rootPath, long totalBytes, CancellationToken ct = default) =>
        UpdateAsync(id, "ready", rootPath, totalBytes, null, completed: true, ct);

    public Task MarkPreparingAsync(Guid id, CancellationToken ct = default) =>
        UpdateAsync(id, "preparing", null, 0, null, completed: false, ct);

    public Task MarkFailedAsync(Guid id, string error, CancellationToken ct = default) =>
        UpdateAsync(id, "failed", null, 0, error, completed: true, ct);

    public Task TouchAsync(Guid id, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var connection = database.CreateConnection();
        connection.Execute(
            "UPDATE adaptive_hls_packages SET last_accessed = @now WHERE id = @id;",
            new { id, now = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture) });
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AdaptiveHlsPackageRecord>> ListAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var connection = database.CreateConnection();
        var rows = connection.Query<AdaptiveHlsPackageRecord>("""
            SELECT id AS Id, asset_id AS AssetId, source_hash AS SourceHash,
                   profile_key AS ProfileKey, status AS Status, root_path AS RootPath,
                   total_bytes AS TotalBytes, created_at AS CreatedAt,
                   last_accessed AS LastAccessed, completed_at AS CompletedAt,
                   last_error AS LastError
            FROM adaptive_hls_packages
            ORDER BY last_accessed ASC;
            """).AsList();
        return Task.FromResult<IReadOnlyList<AdaptiveHlsPackageRecord>>(rows);
    }

    public Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var connection = database.CreateConnection();
        connection.Execute("DELETE FROM adaptive_hls_packages WHERE id = @id;", new { id });
        return Task.CompletedTask;
    }

    private Task UpdateAsync(
        Guid id,
        string status,
        string? rootPath,
        long totalBytes,
        string? error,
        bool completed,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var now = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        using var connection = database.CreateConnection();
        connection.Execute("""
            UPDATE adaptive_hls_packages
            SET status = @status,
                root_path = COALESCE(@rootPath, root_path),
                total_bytes = @totalBytes,
                last_accessed = @now,
                completed_at = CASE WHEN @completed THEN @now ELSE completed_at END,
                last_error = @error
            WHERE id = @id;
            """, new { id, status, rootPath, totalBytes, error, completed, now });
        return Task.CompletedTask;
    }
}

public sealed class AdaptiveHlsPackageRecord
{
    public Guid Id { get; set; }
    public Guid AssetId { get; set; }
    public string SourceHash { get; set; } = string.Empty;
    public string ProfileKey { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string RootPath { get; set; } = string.Empty;
    public long TotalBytes { get; set; }
    public string CreatedAt { get; set; } = string.Empty;
    public string LastAccessed { get; set; } = string.Empty;
    public string? CompletedAt { get; set; }
    public string? LastError { get; set; }
}
