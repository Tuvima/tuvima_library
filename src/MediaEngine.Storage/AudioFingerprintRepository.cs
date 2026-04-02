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

    public async Task UpsertAsync(Guid assetId, byte[] fingerprint, double durationSec, CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        await conn.ExecuteAsync(
            """
            INSERT INTO audio_fingerprints (asset_id, fingerprint, duration_sec, created_at)
            VALUES (@AssetId, @Fingerprint, @DurationSec, datetime('now'))
            ON CONFLICT(asset_id) DO UPDATE SET
                fingerprint = @Fingerprint,
                duration_sec = @DurationSec,
                created_at = datetime('now')
            """,
            new { AssetId = assetId.ToString(), Fingerprint = fingerprint, DurationSec = durationSec });
    }

    public async Task<(byte[]? Fingerprint, double DurationSec)?> GetAsync(Guid assetId, CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        var row = await conn.QueryFirstOrDefaultAsync<FingerprintRow>(
            "SELECT fingerprint, duration_sec AS DurationSec FROM audio_fingerprints WHERE asset_id = @Id",
            new { Id = assetId.ToString() });

        if (row is null) return null;
        return (row.fingerprint, row.DurationSec);
    }

    public async Task<IReadOnlyList<(Guid AssetId, byte[] Fingerprint)>> GetAllAsync(CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        var rows = await conn.QueryAsync<FingerprintAllRow>(
            "SELECT asset_id AS AssetId, fingerprint FROM audio_fingerprints");

        return rows
            .Select(r => (Guid.Parse(r.AssetId), r.fingerprint))
            .ToList();
    }

    public async Task<bool> ExistsAsync(Guid assetId, CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        return await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM audio_fingerprints WHERE asset_id = @Id",
            new { Id = assetId.ToString() }) > 0;
    }

    // Private DTOs to avoid dynamic and boxing issues
    private sealed class FingerprintRow
    {
        public byte[] fingerprint { get; init; } = [];
        public double DurationSec { get; init; }
    }

    private sealed class FingerprintAllRow
    {
        public string AssetId { get; init; } = "";
        public byte[] fingerprint { get; init; } = [];
    }
}
