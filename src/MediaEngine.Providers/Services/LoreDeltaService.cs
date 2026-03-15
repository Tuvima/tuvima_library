using System.Text.Json;
using Microsoft.Extensions.Logging;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Models;

namespace MediaEngine.Providers.Services;

/// <summary>
/// Checks Wikidata <c>lastrevid</c> values for entities in a universe to detect
/// upstream changes since last enrichment.
///
/// <para>
/// Uses the MediaWiki <c>wbgetentities</c> API with <c>props=info</c> to fetch
/// current revision IDs. Compares against cached <see cref="Domain.Entities.FictionalEntity.WikidataRevisionId"/>.
/// </para>
/// </summary>
public sealed class LoreDeltaService : ILoreDeltaService
{
    private const int BatchSize = 50; // MediaWiki API limit per call
    private readonly IFictionalEntityRepository _entityRepo;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<LoreDeltaService> _logger;

    public LoreDeltaService(
        IFictionalEntityRepository entityRepo,
        IHttpClientFactory httpFactory,
        ILogger<LoreDeltaService> logger)
    {
        ArgumentNullException.ThrowIfNull(entityRepo);
        ArgumentNullException.ThrowIfNull(httpFactory);
        ArgumentNullException.ThrowIfNull(logger);
        _entityRepo = entityRepo;
        _httpFactory = httpFactory;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<LoreDeltaResult>> CheckForUpdatesAsync(
        string universeQid, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(universeQid);

        // Load all entities in universe that have a cached revision ID.
        var allEntities = await _entityRepo.GetByUniverseAsync(universeQid, ct)
            .ConfigureAwait(false);
        var tracked = allEntities
            .Where(e => e.WikidataRevisionId.HasValue)
            .ToList();

        if (tracked.Count == 0)
        {
            _logger.LogDebug("No entities with revision IDs in universe {Qid}", universeQid);
            return [];
        }

        _logger.LogInformation(
            "Checking Lore Delta for {Count} entities in universe {Qid}",
            tracked.Count, universeQid);

        var results = new List<LoreDeltaResult>();
        var client = _httpFactory.CreateClient("wikidata_api");

        // Batch-fetch current revision IDs via MediaWiki API.
        for (var i = 0; i < tracked.Count; i += BatchSize)
        {
            ct.ThrowIfCancellationRequested();

            var batch = tracked.Skip(i).Take(BatchSize).ToList();
            var qidList = string.Join("|", batch.Select(e => e.WikidataQid));

            try
            {
                var url = $"https://www.wikidata.org/w/api.php?action=wbgetentities&ids={qidList}&props=info&format=json";
                using var response = await client.GetAsync(url, ct).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);

                if (!doc.RootElement.TryGetProperty("entities", out var entities))
                    continue;

                foreach (var entity in batch)
                {
                    if (!entities.TryGetProperty(entity.WikidataQid, out var entityJson))
                        continue;

                    if (!entityJson.TryGetProperty("lastrevid", out var revElement))
                        continue;

                    var currentRevision = revElement.GetInt64();
                    var cachedRevision = entity.WikidataRevisionId!.Value;
                    var hasChanged = currentRevision != cachedRevision;

                    results.Add(new LoreDeltaResult(
                        entity.WikidataQid,
                        entity.Label,
                        cachedRevision,
                        currentRevision,
                        hasChanged));
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Lore Delta batch check failed for {Count} entities starting at {Qid}",
                    batch.Count, batch[0].WikidataQid);
            }
        }

        var changedCount = results.Count(r => r.HasChanged);
        if (changedCount > 0)
        {
            _logger.LogInformation(
                "Lore Delta: {Changed}/{Total} entities have upstream changes in universe {Qid}",
                changedCount, results.Count, universeQid);
        }

        return results;
    }
}
