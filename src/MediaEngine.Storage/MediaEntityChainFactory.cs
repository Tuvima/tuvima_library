using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Enums;
using MediaEngine.Storage.Contracts;
using MediaEngine.Storage.Services;
using Microsoft.Extensions.Logging;

namespace MediaEngine.Storage;

/// <summary>
/// Creates the Work → Edition chain required before a MediaAsset can be
/// inserted. Idempotent and safe to call concurrently.
///
/// As of Phase 3 (M-082) the chain factory delegates Work resolution to
/// <see cref="HierarchyResolver"/>. The legacy title+author dedup path
/// has been removed entirely — hierarchical media (music, TV, comics,
/// series-bound books) flows through parent/child resolution, and
/// standalone media (movies, single books) gets a fresh Work each time
/// the upstream ingestion logic decides to call us.
///
/// Hub assignment no longer happens at ingestion time. The legacy
/// <c>EnsureContentGroupAsync</c> stub remains as a no-op for DI
/// compatibility and is scheduled for deletion in Phase 4 along with the
/// HubAssignmentService.
/// </summary>
public sealed class MediaEntityChainFactory : IMediaEntityChainFactory
{
    private readonly IDatabaseConnection _db;
    private readonly HierarchyResolver _resolver;
    private readonly ILogger<MediaEntityChainFactory>? _logger;

    public MediaEntityChainFactory(
        IDatabaseConnection db,
        IWorkRepository works,
        IHubRepository hubs,
        HierarchyResolver resolver,
        ILogger<MediaEntityChainFactory>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(works);    // kept for DI compatibility
        ArgumentNullException.ThrowIfNull(hubs);     // kept for DI compatibility
        ArgumentNullException.ThrowIfNull(resolver);
        _db        = db;
        _resolver  = resolver;
        _logger    = logger;
    }

    /// <inheritdoc/>
    public async Task<Guid> EnsureEntityChainAsync(
        MediaType mediaType,
        IReadOnlyDictionary<string, string>? metadata,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        // ── 1. Resolve Work via the hierarchy resolver ───────────────────────
        var resolved = await _resolver.ResolveAsync(mediaType, metadata, ct).ConfigureAwait(false);

        _logger?.LogDebug(
            "Chain factory: resolved {MediaType} → Work {WorkId} ({Kind}, parent={Parent}, ordinal={Ordinal}, new={New})",
            mediaType, resolved.WorkId, resolved.WorkKind, resolved.ParentWorkId, resolved.Ordinal, resolved.NewlyCreated);

        // ── 2. Create Edition under the resolved Work ────────────────────────
        string? formatLabel = null;
        metadata?.TryGetValue("format", out formatLabel);

        var editionId = Guid.NewGuid();

        using var conn = _db.CreateConnection();
        using var insertEdition = conn.CreateCommand();
        insertEdition.CommandText = """
            INSERT INTO editions (id, work_id, format_label)
            VALUES (@id, @work_id, @format_label);
            """;
        insertEdition.Parameters.AddWithValue("@id",           editionId.ToString());
        insertEdition.Parameters.AddWithValue("@work_id",      resolved.WorkId.ToString());
        insertEdition.Parameters.AddWithValue("@format_label",
            formatLabel ?? (object)DBNull.Value);
        insertEdition.ExecuteNonQuery();

        return editionId;
    }
}
