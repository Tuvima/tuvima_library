using MediaEngine.Domain.Models;
using MediaEngine.Providers.Workers;

namespace MediaEngine.Providers.Services;

/// <summary>
/// Read-only editor boundary over the automatic Wikidata bridge worker. Keeping this
/// facade separate prevents the UI from acquiring its own reconciliation algorithm.
/// </summary>
public sealed class WikidataMatchPreviewService(WikidataBridgeWorker worker)
{
    public Task<SearchUniverseResult> PreviewCandidatesAsync(
        Guid entityId,
        string mediaType,
        string? queryOverride,
        IReadOnlyDictionary<string, string>? evidenceOverrides,
        int maxCandidates,
        CancellationToken ct = default) =>
        worker.PreviewCandidatesAsync(
            entityId,
            mediaType,
            queryOverride,
            evidenceOverrides,
            maxCandidates,
            ct);
}
