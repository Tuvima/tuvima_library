using Dapper;
using MediaEngine.Domain.Contracts;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Storage;

public sealed class AudioFingerprintRepository : IAudioFingerprintRepository
{
    private readonly IDatabaseConnection _db;

    public AudioFingerprintRepository(IDatabaseConnection db)
    {
        _db = db;
    }

    public Task UpsertAsync(Guid assetId, byte[] fingerprint, double durationSec, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        conn.Execute(
            """
            INSERT INTO audio_fingerprints (asset_id, fingerprint, duration_sec, created_at)
            VALUES (@AssetId, @Fingerprint, @DurationSec, datetime('now'))
            ON CONFLICT(asset_id) DO UPDATE SET
                fingerprint = @Fingerprint,
                duration_sec = @DurationSec,
                created_at = datetime('now')
            """,
            new { AssetId = assetId, Fingerprint = fingerprint, DurationSec = durationSec });
        return Task.CompletedTask;
    }

    public Task<(byte[]? Fingerprint, double DurationSec)?> GetAsync(Guid assetId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        var row = conn.QueryFirstOrDefault<FingerprintRow>(
            "SELECT fingerprint, duration_sec AS DurationSec FROM audio_fingerprints WHERE asset_id = @Id",
            new { Id = assetId });

        return Task.FromResult<(byte[]? Fingerprint, double DurationSec)?>(
            row is null ? null : (row.fingerprint, row.DurationSec));
    }

    public Task<IReadOnlyList<(Guid AssetId, byte[] Fingerprint)>> GetAllAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        var rows = conn.Query<FingerprintAllRow>(
            "SELECT asset_id AS AssetId, fingerprint FROM audio_fingerprints");

        IReadOnlyList<(Guid AssetId, byte[] Fingerprint)> result = rows
            .Select(r => (r.AssetId, r.fingerprint))
            .ToList();
        return Task.FromResult(result);
    }

    public Task<bool> ExistsAsync(Guid assetId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        var exists = conn.ExecuteScalar<int>(
            "SELECT COUNT(1) FROM audio_fingerprints WHERE asset_id = @Id",
            new { Id = assetId }) > 0;
        return Task.FromResult(exists);
    }

    // Private DTOs to avoid dynamic and boxing issues
    private sealed class FingerprintRow
    {
        public byte[] fingerprint { get; init; } = [];
        public double DurationSec { get; init; }
    }

    private sealed class FingerprintAllRow
    {
        public Guid AssetId { get; init; }
        public byte[] fingerprint { get; init; } = [];
    }
}
