using Microsoft.Data.Sqlite;
using MediaEngine.Domain.Aggregates;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Enums;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Storage;

/// <summary>
/// ORM-less SQLite implementation of <see cref="IMediaAssetRepository"/>.
/// All SQL is executed via raw <see cref="SqliteCommand"/>; no reflection or
/// expression trees are used.
///
/// Thread safety: <see cref="IDatabaseConnection.Open"/> returns a single shared
/// <see cref="SqliteConnection"/> (SQLite is serialised within a process by the
/// driver).  Each public method creates its own <see cref="SqliteCommand"/> and
/// disposes it before returning, so concurrent calls are safe as long as the
/// connection itself is not disposed mid-call.
///
/// Spec: Phase 4 – Hash Dominance (content_hash UNIQUE + INSERT OR IGNORE);
///       Phase 7 – Asset lifecycle status transitions.
/// </summary>
public sealed class MediaAssetRepository : IMediaAssetRepository
{
    private readonly IDatabaseConnection _db;

    public MediaAssetRepository(IDatabaseConnection db)
    {
        ArgumentNullException.ThrowIfNull(db);
        _db = db;
    }

    // -------------------------------------------------------------------------
    // IMediaAssetRepository
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public Task<MediaAsset?> FindByHashAsync(string contentHash, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(contentHash);

        var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, edition_id, content_hash, file_path_root, status
            FROM   media_assets
            WHERE  content_hash = @content_hash
            LIMIT  1;
            """;
        cmd.Parameters.AddWithValue("@content_hash", contentHash);

        using var reader = cmd.ExecuteReader();
        var result = reader.Read() ? MapRow(reader) : null;
        return Task.FromResult(result);
    }

    /// <inheritdoc/>
    public Task<MediaAsset?> FindByPathRootAsync(string pathRoot, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(pathRoot);

        var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, edition_id, content_hash, file_path_root, status
            FROM   media_assets
            WHERE  file_path_root = @path
            LIMIT  1;
            """;
        cmd.Parameters.AddWithValue("@path", pathRoot);

        using var reader = cmd.ExecuteReader();
        var result = reader.Read() ? MapRow(reader) : null;
        return Task.FromResult(result);
    }

    /// <inheritdoc/>
    public Task<MediaAsset?> FindByIdAsync(Guid id, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, edition_id, content_hash, file_path_root, status
            FROM   media_assets
            WHERE  id = @id
            LIMIT  1;
            """;
        cmd.Parameters.AddWithValue("@id", id.ToString());

        using var reader = cmd.ExecuteReader();
        var result = reader.Read() ? MapRow(reader) : null;
        return Task.FromResult(result);
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

        var conn = _db.Open();

        // Step 1: attempt the insert.
        using var insertCmd = conn.CreateCommand();
        insertCmd.CommandText = """
            INSERT OR IGNORE INTO media_assets
                (id, edition_id, content_hash, file_path_root, status)
            VALUES
                (@id, @edition_id, @content_hash, @file_path_root, @status);
            """;
        insertCmd.Parameters.AddWithValue("@id",             asset.Id.ToString());
        insertCmd.Parameters.AddWithValue("@edition_id",     asset.EditionId.ToString());
        insertCmd.Parameters.AddWithValue("@content_hash",   asset.ContentHash);
        insertCmd.Parameters.AddWithValue("@file_path_root", asset.FilePathRoot);
        insertCmd.Parameters.AddWithValue("@status",         asset.Status.ToString());
        insertCmd.ExecuteNonQuery();

        // Step 2: check whether the row was actually written.
        // changes() returns 1 if a row was inserted, 0 if IGNORE fired.
        using var changesCmd = conn.CreateCommand();
        changesCmd.CommandText = "SELECT changes();";
        var changes = Convert.ToInt64(changesCmd.ExecuteScalar()!);

        return Task.FromResult(changes > 0);
    }

    /// <inheritdoc/>
    public Task UpdateStatusAsync(Guid id, AssetStatus status, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE media_assets
            SET    status = @status
            WHERE  id     = @id;
            """;
        cmd.Parameters.AddWithValue("@status", status.ToString());
        cmd.Parameters.AddWithValue("@id",     id.ToString());
        cmd.ExecuteNonQuery();

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task UpdateFilePathAsync(Guid id, string newPath, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(newPath);

        var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE media_assets
            SET    file_path_root = @path
            WHERE  id             = @id;
            """;
        cmd.Parameters.AddWithValue("@path", newPath);
        cmd.Parameters.AddWithValue("@id",   id.ToString());
        cmd.ExecuteNonQuery();

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM media_assets WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", id.ToString());
        cmd.ExecuteNonQuery();

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<MediaAsset>> ListByStatusAsync(AssetStatus status, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, edition_id, content_hash, file_path_root, status
            FROM   media_assets
            WHERE  status = @status;
            """;
        cmd.Parameters.AddWithValue("@status", status.ToString());

        using var reader = cmd.ExecuteReader();
        var results = new List<MediaAsset>();
        while (reader.Read())
            results.Add(MapRow(reader));

        return Task.FromResult<IReadOnlyList<MediaAsset>>(results);
    }

    // -------------------------------------------------------------------------
    // Private row mapper
    // -------------------------------------------------------------------------

    /// <summary>
    /// Maps the current <see cref="SqliteDataReader"/> row to a <see cref="MediaAsset"/>.
    /// Column ordinals match the SELECT list used in every query in this class:
    ///   0 = id, 1 = edition_id, 2 = content_hash, 3 = file_path_root, 4 = status
    /// </summary>
    private static MediaAsset MapRow(SqliteDataReader r) => new()
    {
        Id           = Guid.Parse(r.GetString(0)),
        EditionId    = Guid.Parse(r.GetString(1)),
        ContentHash  = r.GetString(2),
        FilePathRoot = r.GetString(3),
        // The CHECK constraint in schema.sql ensures status is one of the three
        // valid enum names; Enum.Parse is safe to call without a try/catch here.
        Status       = Enum.Parse<AssetStatus>(r.GetString(4), ignoreCase: true),
    };
}
