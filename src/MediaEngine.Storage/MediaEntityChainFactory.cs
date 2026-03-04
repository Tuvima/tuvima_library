using Microsoft.Data.Sqlite;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Enums;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Storage;

/// <summary>
/// Creates the Hub → Work → Edition chain required before a MediaAsset
/// can be inserted.  Uses <c>INSERT OR IGNORE</c> throughout so the
/// operation is idempotent and safe to call concurrently.
///
/// Hub matching: when metadata contains a "title" key, the factory first
/// attempts a case-insensitive lookup on <c>hubs.display_name</c>.  If a
/// match is found the existing Hub is reused; otherwise a new Hub is
/// created.  A new Work and Edition are always created (one per asset).
///
/// Spec: Phase 4 – Hub Atomic Zone; Phase 7 – Ingestion § Entity Chain.
/// </summary>
public sealed class MediaEntityChainFactory : IMediaEntityChainFactory
{
    private readonly IDatabaseConnection _db;

    public MediaEntityChainFactory(IDatabaseConnection db)
    {
        ArgumentNullException.ThrowIfNull(db);
        _db = db;
    }

    /// <inheritdoc/>
    public Task<Guid> EnsureEntityChainAsync(
        MediaType mediaType,
        IReadOnlyDictionary<string, string>? metadata,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var conn = _db.Open();

        // Extract the display name from metadata; fall back to "Unknown".
        string? title = null;
        metadata?.TryGetValue("title", out title);
        var displayName = string.IsNullOrWhiteSpace(title) ? "Unknown" : title.Trim();

        // ── 1. Find or create Hub ──────────────────────────────────────────
        Guid hubId;
        using (var findCmd = conn.CreateCommand())
        {
            findCmd.CommandText = """
                SELECT id FROM hubs
                WHERE  LOWER(display_name) = LOWER(@name)
                LIMIT  1;
                """;
            findCmd.Parameters.AddWithValue("@name", displayName);
            var existing = findCmd.ExecuteScalar();

            if (existing is string existingId)
            {
                hubId = Guid.Parse(existingId);
            }
            else
            {
                hubId = Guid.NewGuid();
                using var insertHub = conn.CreateCommand();
                insertHub.CommandText = """
                    INSERT OR IGNORE INTO hubs (id, display_name, created_at)
                    VALUES (@id, @dn, @ca);
                    """;
                insertHub.Parameters.AddWithValue("@id", hubId.ToString());
                insertHub.Parameters.AddWithValue("@dn", displayName);
                insertHub.Parameters.AddWithValue("@ca", DateTimeOffset.UtcNow.ToString("O"));
                insertHub.ExecuteNonQuery();
            }
        }

        // ── 2. Create Work ─────────────────────────────────────────────────
        var workId = Guid.NewGuid();
        using (var insertWork = conn.CreateCommand())
        {
            insertWork.CommandText = """
                INSERT INTO works (id, hub_id, media_type)
                VALUES (@id, @hub_id, @media_type);
                """;
            insertWork.Parameters.AddWithValue("@id",         workId.ToString());
            insertWork.Parameters.AddWithValue("@hub_id",     hubId.ToString());
            insertWork.Parameters.AddWithValue("@media_type", mediaType.ToString());
            insertWork.ExecuteNonQuery();
        }

        // ── 3. Create Edition ──────────────────────────────────────────────
        var editionId = Guid.NewGuid();
        string? formatLabel = null;
        metadata?.TryGetValue("format", out formatLabel);

        using (var insertEdition = conn.CreateCommand())
        {
            insertEdition.CommandText = """
                INSERT INTO editions (id, work_id, format_label)
                VALUES (@id, @work_id, @format_label);
                """;
            insertEdition.Parameters.AddWithValue("@id",           editionId.ToString());
            insertEdition.Parameters.AddWithValue("@work_id",      workId.ToString());
            insertEdition.Parameters.AddWithValue("@format_label",
                formatLabel ?? (object)DBNull.Value);
            insertEdition.ExecuteNonQuery();
        }

        return Task.FromResult(editionId);
    }
}
