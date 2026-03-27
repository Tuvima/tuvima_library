using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Enums;
using MediaEngine.Storage.Contracts;
using MediaEngine.Storage.Models;
using Microsoft.Extensions.Logging;

namespace MediaEngine.Providers.Services;

/// <summary>
/// Stage 0: Local Match service — checks the <c>bridge_ids</c> table and
/// canonical values for an existing library match before making any external
/// API calls.
///
/// <para>
/// Bridge ID lookups are indexed on <c>(id_type, id_value)</c> making them
/// sub-millisecond. For episodic content (TV episodes, album tracks, podcast
/// episodes), a single Stage 0 hit avoids the full two-stage pipeline for
/// every sibling item.
/// </para>
///
/// <list type="bullet">
///   <item><b>Step 1:</b> Exact ID match against <c>bridge_ids</c> table.
///     Checked in priority order: isbn, isbn_13, asin, apple_books_id,
///     tmdb_id, imdb_id, musicbrainz_id, comic_vine_id.</item>
///   <item><b>Step 2:</b> (Placeholder) Fuzzy title+author match against
///     canonical_values using native Levenshtein distance. Only exact ID matching
///     is active now — covers the highest-impact cases (episodic series,
///     ISBN-tagged books, ASIN audiobooks).</item>
/// </list>
///
/// Spec: §3.21 — Cross-Media Metadata Strategy (Retail-First pipeline).
/// </summary>
public sealed class LocalMatchService : ILocalMatchService
{
    private readonly IBridgeIdRepository _bridgeIdRepo;
    private readonly ICanonicalValueRepository _canonicalRepo;
    private readonly IConfigurationLoader _configLoader;
    private readonly ILogger<LocalMatchService> _logger;

    // Bridge ID types checked in priority order.
    // Higher-specificity identifiers (ISBN, ASIN) are checked before
    // lower-specificity ones (TMDB, IMDb) to minimise false positives.
    private static readonly string[] BridgeIdPriorityOrder =
    [
        "isbn",
        "isbn_13",
        "asin",
        "apple_books_id",
        "tmdb_id",
        "imdb_id",
        "musicbrainz_id",
        "comic_vine_id",
    ];

    public LocalMatchService(
        IBridgeIdRepository bridgeIdRepo,
        ICanonicalValueRepository canonicalRepo,
        IConfigurationLoader configLoader,
        ILogger<LocalMatchService> logger)
    {
        _bridgeIdRepo  = bridgeIdRepo;
        _canonicalRepo = canonicalRepo;
        _configLoader  = configLoader;
        _logger        = logger;
    }

    /// <inheritdoc/>
    public async Task<LocalMatchResult> TryMatchAsync(
        IReadOnlyDictionary<string, string> hints,
        MediaType mediaType,
        CancellationToken ct = default)
    {
        var hydration = _configLoader.LoadHydration();

        if (!hydration.LocalMatchEnabled)
        {
            _logger.LogDebug("Stage 0: Local match disabled via config — skipping.");
            return LocalMatchResult.NotFound;
        }

        // ── Step 1: Exact ID match against bridge_ids table ───────────────────
        // Walk the priority list; return on first hit to keep this path fast.
        foreach (var idType in BridgeIdPriorityOrder)
        {
            if (!hints.TryGetValue(idType, out var idValue)
                || string.IsNullOrWhiteSpace(idValue))
                continue;

            IReadOnlyList<BridgeIdEntry> matches =
                await _bridgeIdRepo.FindByValueAsync(idType, idValue, ct).ConfigureAwait(false);

            if (matches.Count == 0)
                continue;

            // Take the first entry — the repository orders by most recent.
            var match = matches[0];

            _logger.LogInformation(
                "Stage 0: Local match found via bridge ID {IdType}={IdValue} → entity {EntityId}",
                idType, idValue, match.EntityId);

            return new LocalMatchResult
            {
                Found           = true,
                EntityId        = match.EntityId,
                MatchedByIdType = idType,
                IsExactIdMatch  = true,
            };
        }

        // ── Step 2: Fuzzy title+author match (placeholder) ────────────────────
        // Full implementation: query canonical_values by "title" key with
        // case-insensitive comparison, then apply native Levenshtein ratio against
        // the hint title with a threshold of LocalMatchFuzzyThreshold (0.95).
        // Deferred — exact ID matching covers the highest-impact cases for now.

        _logger.LogDebug(
            "Stage 0: No local match found for media type {MediaType} with hint keys: {Keys}",
            mediaType,
            string.Join(", ", hints.Keys));

        return LocalMatchResult.NotFound;
    }
}
