using Dapper;
using MediaEngine.Domain.Aggregates;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Enums;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Storage;

/// <summary>
/// SQLite implementation of <see cref="IMediaAssetRepository"/>.
/// Uses Dapper for type-safe column-to-property mapping.
///
/// Spec: Phase 4 – Hash Dominance (content_hash UNIQUE + INSERT OR IGNORE);
///       Phase 7 – Asset lifecycle status transitions.
/// </summary>
public sealed class MediaAssetRepository : IMediaAssetRepository
{
    private readonly IDatabaseConnection _db;

    /// <summary>
    /// Flat projection used by Dapper to read rows. The <c>Status</c> column is
    /// stored as TEXT in SQLite; we capture it as a string and convert to the
    /// <see cref="AssetStatus"/> enum in <see cref="ToAsset"/>.
    /// </summary>
    private sealed record MediaAssetRow(
        string Id,
        string EditionId,
        string ContentHash,
        string FilePathRoot,
        string Status);

    private static MediaAsset ToAsset(MediaAssetRow r) => new()
    {
        Id           = Guid.Parse(r.Id),
        EditionId    = Guid.Parse(r.EditionId),
        ContentHash  = r.ContentHash,
        FilePathRoot = r.FilePathRoot,
        Status       = Enum.Parse<AssetStatus>(r.Status, ignoreCase: true),
    };

    private const string SelectColumns = """
        id             AS Id,
        edition_id     AS EditionId,
        content_hash   AS ContentHash,
        file_path_root AS FilePathRoot,
        status         AS Status
        """;

    public MediaAssetRepository(IDatabaseConnection db)
    {
        ArgumentNullException.ThrowIfNull(db);
        _db = db;
    }

    /// <inheritdoc/>
    public Task<MediaAsset?> FindByHashAsync(string contentHash, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(contentHash);

        using var conn = _db.CreateConnection();
        var row = conn.QueryFirstOrDefault<MediaAssetRow>($"""
            SELECT {SelectColumns}
            FROM   media_assets
            WHERE  content_hash = @contentHash
            LIMIT  1;
            """, new { contentHash });

        return Task.FromResult(row is null ? null : (MediaAsset?)ToAsset(row));
    }

    /// <inheritdoc/>
    public Task<MediaAsset?> FindByPathRootAsync(string pathRoot, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(pathRoot);

        using var conn = _db.CreateConnection();
        var row = conn.QueryFirstOrDefault<MediaAssetRow>($"""
            SELECT {SelectColumns}
            FROM   media_assets
            WHERE  file_path_root = @pathRoot
            LIMIT  1;
            """, new { pathRoot });

        return Task.FromResult(row is null ? null : (MediaAsset?)ToAsset(row));
    }

    /// <inheritdoc/>
    public Task<MediaAsset?> FindByIdAsync(Guid id, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        var row = conn.QueryFirstOrDefault<MediaAssetRow>($"""
            SELECT {SelectColumns}
            FROM   media_assets
            WHERE  id = @id
            LIMIT  1;
            """, new { id = id.ToString() });

        return Task.FromResult(row is null ? null : (MediaAsset?)ToAsset(row));
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Uses <c>INSERT OR IGNORE</c> on the <c>content_hash</c> unique constraint.
    /// If the hash already exists the insert is silently skipped and the method
    /// returns <see langword="false"/> — no exception is thrown.
    ///
    /// After the INSERT, <c>SELECT changes()</c> is called on the same connection.
    /// Because SQLite serialises all operations on a single connection,
    /// <c>changes()</c> reliably reflects the row count of the immediately
    /// preceding statement.
    /// </remarks>
    public Task<bool> InsertAsync(MediaAsset asset, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(asset);

        using var conn = _db.CreateConnection();

        // Step 1: attempt the insert.
        conn.Execute("""
            INSERT OR IGNORE INTO media_assets
                (id, edition_id, content_hash, file_path_root, status)
            VALUES
                (@id, @editionId, @contentHash, @filePathRoot, @status);
            """,
            new
            {
                id           = asset.Id.ToString(),
                editionId    = asset.EditionId.ToString(),
                contentHash  = asset.ContentHash,
                filePathRoot = asset.FilePathRoot,
                status       = asset.Status.ToString(),
            });

        // Step 2: changes() returns 1 if a row was inserted, 0 if IGNORE fired.
        var changes = conn.ExecuteScalar<long>("SELECT changes();");
        return Task.FromResult(changes > 0);
    }

    /// <inheritdoc/>
    public Task UpdateStatusAsync(Guid id, AssetStatus status, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        conn.Execute("""
            UPDATE media_assets
            SET    status = @status
            WHERE  id     = @id;
            """,
            new
            {
                status = status.ToString(),
                id     = id.ToString(),
            });

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task UpdateFilePathAsync(Guid id, string newPath, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(newPath);

        using var conn = _db.CreateConnection();
        conn.Execute("""
            UPDATE media_assets
            SET    file_path_root = @path
            WHERE  id             = @id;
            """,
            new
            {
                path = newPath,
                id   = id.ToString(),
            });

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        conn.Execute(
            "DELETE FROM media_assets WHERE id = @id;",
            new { id = id.ToString() });

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<MediaAsset>> ListByStatusAsync(AssetStatus status, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        var rows = conn.Query<MediaAssetRow>($"""
            SELECT {SelectColumns}
            FROM   media_assets
            WHERE  status = @status;
            """, new { status = status.ToString() }).AsList();

        return Task.FromResult<IReadOnlyList<MediaAsset>>(rows.Select(ToAsset).ToList());
    }

    /// <inheritdoc/>
    public Task<MediaAsset?> FindFirstByWorkIdAsync(Guid workId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        var row = conn.QueryFirstOrDefault<MediaAssetRow>($"""
            SELECT ma.id             AS Id,
                   ma.edition_id     AS EditionId,
                   ma.content_hash   AS ContentHash,
                   ma.file_path_root AS FilePathRoot,
                   ma.status         AS Status
            FROM   media_assets ma
            JOIN   editions e ON e.id = ma.edition_id
            WHERE  e.work_id = @workId
              AND  ma.status = 'Normal'
            LIMIT  1;
            """, new { workId = workId.ToString() });

        return Task.FromResult(row is null ? null : (MediaAsset?)ToAsset(row));
    }
}
