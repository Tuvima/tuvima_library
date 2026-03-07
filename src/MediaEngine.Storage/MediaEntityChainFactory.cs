using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Enums;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Storage;

/// <summary>
/// Creates the Work → Edition chain required before a MediaAsset
/// can be inserted.  Uses <c>INSERT OR IGNORE</c> throughout so the
/// operation is idempotent and safe to call concurrently.
///
/// Hub assignment no longer happens at ingestion time — Works are created
/// standalone (hub_id = NULL). Hub intelligence runs during Stage 2 of the
/// hydration pipeline, where Wikidata relationship properties drive grouping.
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

        // ── 1. Create Work (standalone — no Hub) ─────────────────────────
        var workId = Guid.NewGuid();
        using (var insertWork = conn.CreateCommand())
        {
            insertWork.CommandText = """
                INSERT INTO works (id, hub_id, media_type, wikidata_status)
                VALUES (@id, NULL, @media_type, 'pending');
                """;
            insertWork.Parameters.AddWithValue("@id",         workId.ToString());
            insertWork.Parameters.AddWithValue("@media_type", mediaType.ToString());
            insertWork.ExecuteNonQuery();
        }

        // ── 2. Create Edition ──────────────────────────────────────────────
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
