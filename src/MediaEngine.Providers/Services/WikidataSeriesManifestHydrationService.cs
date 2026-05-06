using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MediaEngine.Domain.Aggregates;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Models;
using MediaEngine.Providers.Models;
using MediaEngine.Storage.Contracts;
using Microsoft.Extensions.Logging;
using Tuvima.Wikidata;

namespace MediaEngine.Providers.Services;

public sealed class WikidataSeriesManifestHydrationService
{
    private readonly WikidataReconciler _reconciler;
    private readonly ISeriesManifestRepository _manifestRepo;
    private readonly ICollectionRepository _collectionRepo;
    private readonly IReviewQueueRepository _reviewRepo;
    private readonly IConfigurationLoader _configLoader;
    private readonly ILogger<WikidataSeriesManifestHydrationService> _logger;

    private readonly ConcurrentDictionary<string, Lazy<Task>> _inFlight = new(StringComparer.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
    };

    public WikidataSeriesManifestHydrationService(
        WikidataReconciler reconciler,
        ISeriesManifestRepository manifestRepo,
        ICollectionRepository collectionRepo,
        IReviewQueueRepository reviewRepo,
        IConfigurationLoader configLoader,
        ILogger<WikidataSeriesManifestHydrationService> logger)
    {
        _reconciler = reconciler;
        _manifestRepo = manifestRepo;
        _collectionRepo = collectionRepo;
        _reviewRepo = reviewRepo;
        _configLoader = configLoader;
        _logger = logger;
    }

    public async Task HydrateAsync(SeriesManifestHydrationContext context, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!ShouldHydrate(context.MediaType))
            return;

        var (seriesQid, seriesLabel) = await ResolveSeriesQidAsync(context, ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(seriesQid))
            return;

        var cacheKey = $"{context.IngestionRunId?.ToString("D") ?? "process"}:{seriesQid}";
        var lazy = _inFlight.GetOrAdd(
            cacheKey,
            _ => new Lazy<Task>(() => HydrateCoreAsync(context, seriesQid, seriesLabel, ct)));

        try
        {
            await lazy.Value.ConfigureAwait(false);
        }
        finally
        {
            if (lazy.IsValueCreated && lazy.Value.IsCompleted)
                _inFlight.TryRemove(cacheKey, out _);
        }
    }

    private async Task HydrateCoreAsync(
        SeriesManifestHydrationContext context,
        string seriesQid,
        string? seriesLabel,
        CancellationToken ct)
    {
        try
        {
            var ttlDays = Math.Max(1, _configLoader.LoadHydration().SeriesManifestRefreshDays);
            var cachedHydration = await _manifestRepo.GetHydrationAsync(seriesQid, ct).ConfigureAwait(false);

            if (cachedHydration is not null
                && DateTimeOffset.UtcNow - cachedHydration.LastHydratedAt < TimeSpan.FromDays(ttlDays))
            {
                var cachedItems = await _manifestRepo.GetItemsBySeriesQidAsync(seriesQid, ct).ConfigureAwait(false);
                if (cachedItems.Count > 0
                    && cachedItems.Any(i => string.Equals(i.ItemQid, context.ResolvedWorkQid, StringComparison.OrdinalIgnoreCase)))
                {
                    var relinked = await RelinkItemsAsync(
                        cachedHydration.CollectionId,
                        cachedItems,
                        context,
                        createReviews: true,
                        ct).ConfigureAwait(false);

                    await _manifestRepo.UpsertManifestAsync(cachedHydration.WithUpdatedTimestamp(), relinked, ct)
                        .ConfigureAwait(false);
                    await _manifestRepo.LinkOwnedWorksAsync(cachedHydration.CollectionId, relinked, ct)
                        .ConfigureAwait(false);

                    _logger.LogDebug(
                        "Series manifest cache hit for {SeriesQid}; relinked {Count} cached rows",
                        seriesQid,
                        relinked.Count);
                    return;
                }
            }

            var language = NormalizeLanguage(_configLoader.LoadCore().Language.Metadata);
            var manifest = await _reconciler.Series.GetManifestAsync(new SeriesManifestRequest
            {
                SeriesQid = seriesQid,
                Language = language,
                IncludeCollections = true,
                ExpandCollections = true,
                IncludePublicationDate = true,
                IncludeDescriptions = false,
                MaxDepth = 2,
                MaxItems = 500,
            }, ct).ConfigureAwait(false);

            var collection = await UpsertCollectionAsync(
                manifest.SeriesQid,
                FirstNonBlank(manifest.SeriesLabel, seriesLabel, context.SeriesHint, manifest.SeriesQid),
                ct).ConfigureAwait(false);

            var now = DateTimeOffset.UtcNow;
            var itemRecords = manifest.Items
                .Select((item, index) => ToRecord(collection.Id, manifest.SeriesQid, context.MediaType, item, index, now))
                .ToList();

            itemRecords = await RelinkItemsAsync(collection.Id, itemRecords, context, createReviews: true, ct)
                .ConfigureAwait(false);

            if (itemRecords.Count > 0
                && !itemRecords.Any(i => string.Equals(i.ItemQid, context.ResolvedWorkQid, StringComparison.OrdinalIgnoreCase)))
            {
                await CreateReviewAsync(
                    context.WorkId ?? context.Lineage?.TargetForSelfScope ?? context.AssetId,
                    $"Resolved work {context.ResolvedWorkQid} claims series {manifest.SeriesQid}, but it was not present in the fetched Wikidata series manifest.",
                    ct).ConfigureAwait(false);
            }

            foreach (var warning in manifest.Warnings.Where(IsSeriousWarning))
            {
                await CreateReviewAsync(
                    context.WorkId ?? context.Lineage?.TargetForSelfScope ?? context.AssetId,
                    $"Wikidata series manifest warning for {manifest.SeriesQid}: {warning.Code} - {warning.Message}",
                    ct).ConfigureAwait(false);
            }

            var warningsJson = JsonSerializer.Serialize(
                manifest.Warnings.Select(w => new SeriesManifestWarningDto
                {
                    Code = w.Code,
                    Message = w.Message,
                    Qid = w.Qid,
                }).ToList(),
                JsonOptions);

            var itemQids = itemRecords
                .Select(i => i.ItemQid)
                .OrderBy(q => q, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var manifestJson = JsonSerializer.Serialize(manifest, JsonOptions);
            var hydration = new SeriesManifestHydration
            {
                SeriesQid = manifest.SeriesQid,
                CollectionId = collection.Id,
                SeriesLabel = manifest.SeriesLabel ?? seriesLabel,
                ManifestVersion = typeof(SeriesManifestRequest).Assembly.GetName().Version?.ToString(),
                ManifestHash = Hash(manifestJson),
                KnownItemQidsHash = Hash(string.Join('\n', itemQids)),
                WarningsJson = warningsJson,
                ApiMetadataJson = JsonSerializer.Serialize(new
                {
                    language,
                    includeCollections = true,
                    expandCollections = true,
                    includePublicationDate = true,
                    includeDescriptions = false,
                    maxDepth = 2,
                    maxItems = 500,
                    completeness = manifest.Completeness.ToString(),
                }, JsonOptions),
                LastHydratedAt = now,
                CreatedAt = cachedHydration?.CreatedAt ?? now,
                UpdatedAt = now,
            };

            await _manifestRepo.UpsertManifestAsync(hydration, itemRecords, ct).ConfigureAwait(false);
            await _manifestRepo.LinkOwnedWorksAsync(collection.Id, itemRecords, ct).ConfigureAwait(false);

            _logger.LogInformation(
                "Hydrated Wikidata series manifest {SeriesQid} ({SeriesLabel}) with {Count} named items",
                manifest.SeriesQid,
                manifest.SeriesLabel ?? seriesLabel,
                itemRecords.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex,
                "Series manifest hydration failed for resolved QID {Qid} ({Title}); ingestion will continue",
                context.ResolvedWorkQid,
                context.Title);
        }
    }

    private async Task<(string? Qid, string? Label)> ResolveSeriesQidAsync(
        SeriesManifestHydrationContext context,
        CancellationToken ct)
    {
        if (TryResolveFromClaims(context.FullClaims, out var qid, out var label))
            return (qid, label);

        if (context.Lineage is not null)
        {
            var collectionId = await _collectionRepo.GetCollectionIdByWorkIdAsync(
                context.Lineage.TargetForParentScope,
                ct).ConfigureAwait(false);

            if (collectionId is not null)
            {
                var collection = await _collectionRepo.GetByIdAsync(collectionId.Value, ct).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(collection?.WikidataQid))
                    return (collection.WikidataQid, collection.DisplayName);
            }
        }

        return (null, null);
    }

    private static bool TryResolveFromClaims(
        IReadOnlyList<ProviderClaim> claims,
        out string? qid,
        out string? label)
    {
        foreach (var key in new[] { "series_qid", "part_of_the_series_qid", "part_of_series_qid" })
        {
            var claim = claims.FirstOrDefault(c => string.Equals(c.Key, key, StringComparison.OrdinalIgnoreCase));
            if (claim is not null && TryParseQidValue(claim.Value, out qid, out label))
                return true;
        }

        foreach (var key in new[] { "series" })
        {
            var claim = claims.FirstOrDefault(c => string.Equals(c.Key, key, StringComparison.OrdinalIgnoreCase));
            if (claim is not null && TryParseQidValue(claim.Value, out qid, out label))
                return true;
        }

        qid = null;
        label = null;
        return false;
    }

    private static bool TryParseQidValue(string? value, out string? qid, out string? label)
    {
        qid = null;
        label = null;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var trimmed = value.Trim();
        var parts = trimmed.Split("::", 2, StringSplitOptions.TrimEntries);
        var qidPart = parts[0];
        var slashIndex = qidPart.LastIndexOf('/');
        if (slashIndex >= 0)
            qidPart = qidPart[(slashIndex + 1)..];

        if (qidPart.Length > 1 && qidPart[0] is 'Q' && qidPart.Skip(1).All(char.IsDigit))
        {
            qid = qidPart;
            label = parts.Length > 1 ? parts[1] : null;
            return true;
        }

        return false;
    }

    private async Task<Collection> UpsertCollectionAsync(string seriesQid, string label, CancellationToken ct)
    {
        var existing = await _collectionRepo.FindByQidAsync(seriesQid, ct).ConfigureAwait(false);
        if (existing is not null)
            return existing;

        var collection = new Collection
        {
            Id = Guid.NewGuid(),
            DisplayName = string.IsNullOrWhiteSpace(label) ? seriesQid : label,
            WikidataQid = seriesQid,
            CollectionType = "ContentGroup",
            Resolution = "materialized",
            Scope = "library",
            UniverseStatus = "Unknown",
            IconName = "collections_bookmark",
            CreatedAt = DateTimeOffset.UtcNow,
            SortField = "sort_order",
            SortDirection = "asc",
        };

        await _collectionRepo.UpsertAsync(collection, ct).ConfigureAwait(false);
        return collection;
    }

    private async Task<List<SeriesManifestItemRecord>> RelinkItemsAsync(
        Guid collectionId,
        IReadOnlyList<SeriesManifestItemRecord> items,
        SeriesManifestHydrationContext context,
        bool createReviews,
        CancellationToken ct)
    {
        var workMatches = await _manifestRepo.FindWorkIdsByQidsAsync(
            items.Select(i => i.ItemQid).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            ct).ConfigureAwait(false);

        var relinked = new List<SeriesManifestItemRecord>(items.Count);
        foreach (var item in items)
        {
            workMatches.TryGetValue(item.ItemQid, out var matches);
            matches ??= [];

            if (matches.Count == 0
                && string.Equals(item.ItemQid, context.ResolvedWorkQid, StringComparison.OrdinalIgnoreCase)
                && context.WorkId.HasValue)
            {
                matches = [context.WorkId.Value];
            }

            var state = matches.Count switch
            {
                0 => "Missing",
                1 => "Owned",
                _ => "Ambiguous",
            };

            if (state == "Ambiguous" && createReviews)
            {
                await CreateReviewAsync(
                    context.WorkId ?? context.Lineage?.TargetForSelfScope ?? context.AssetId,
                    $"Multiple local works share Wikidata QID {item.ItemQid} while linking series manifest item '{item.ItemLabel ?? item.ItemQid}'.",
                    ct).ConfigureAwait(false);
            }

            relinked.Add(item.WithOwnership(collectionId, state, matches.Count == 1 ? matches[0] : null));
        }

        return relinked;
    }

    private static SeriesManifestItemRecord ToRecord(
        Guid collectionId,
        string seriesQid,
        MediaType contextMediaType,
        SeriesManifestItem item,
        int index,
        DateTimeOffset now)
    {
        var sortOrder = item.ParsedSeriesOrdinal.HasValue
            ? (double)item.ParsedSeriesOrdinal.Value
            : index + 1;

        return new SeriesManifestItemRecord
        {
            Id = Guid.NewGuid(),
            CollectionId = collectionId,
            SeriesQid = seriesQid,
            ItemQid = item.Qid,
            ItemLabel = string.IsNullOrWhiteSpace(item.Label) ? item.Qid : item.Label,
            ItemDescription = item.Description,
            MediaType = contextMediaType.ToString(),
            RawOrdinal = item.RawSeriesOrdinal,
            ParsedOrdinal = item.ParsedSeriesOrdinal.HasValue ? (double)item.ParsedSeriesOrdinal.Value : null,
            SortOrder = sortOrder,
            PublicationDate = item.PublicationDate?.ToString("O"),
            PreviousQid = item.PreviousQid,
            NextQid = item.NextQid,
            ParentCollectionQid = item.ParentCollectionQid,
            ParentCollectionLabel = item.ParentCollectionLabel,
            IsCollection = item.IsCollection,
            IsExpandedFromCollection = item.IsExpandedFromCollection,
            SourcePropertiesJson = JsonSerializer.Serialize(item.SourceProperties, JsonOptions),
            RelationshipsJson = JsonSerializer.Serialize(item.Relationships, JsonOptions),
            OrderSource = item.OrderSource.ToString(),
            OwnershipState = "Missing",
            LastHydratedAt = now,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    private async Task CreateReviewAsync(Guid entityId, string detail, CancellationToken ct)
    {
        await _reviewRepo.InsertAsync(new ReviewQueueEntry
        {
            Id = Guid.NewGuid(),
            EntityId = entityId,
            EntityType = "Work",
            Trigger = ReviewTrigger.MetadataConflict,
            Status = ReviewStatus.Pending,
            Detail = detail,
            CreatedAt = DateTimeOffset.UtcNow,
        }, ct).ConfigureAwait(false);
    }

    private static bool ShouldHydrate(MediaType mediaType)
        => mediaType is MediaType.Books or MediaType.Audiobooks or MediaType.Comics or MediaType.Movies or MediaType.TV;

    private static bool IsSeriousWarning(SeriesManifestWarning warning)
        => warning.Code.Contains("conflict", StringComparison.OrdinalIgnoreCase)
           || warning.Code.Contains("broken", StringComparison.OrdinalIgnoreCase)
           || warning.Code.Contains("chain", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeLanguage(string? value)
        => string.IsNullOrWhiteSpace(value) ? "en" : value.Split('-', '_')[0].ToLowerInvariant();

    private static string FirstNonBlank(params string?[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? "Unknown series";

    private static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}

internal static class SeriesManifestRecordExtensions
{
    public static SeriesManifestHydration WithUpdatedTimestamp(this SeriesManifestHydration hydration)
    {
        var now = DateTimeOffset.UtcNow;
        return new SeriesManifestHydration
        {
            SeriesQid = hydration.SeriesQid,
            CollectionId = hydration.CollectionId,
            SeriesLabel = hydration.SeriesLabel,
            ManifestSource = hydration.ManifestSource,
            ManifestVersion = hydration.ManifestVersion,
            ManifestHash = hydration.ManifestHash,
            KnownItemQidsHash = hydration.KnownItemQidsHash,
            WarningsJson = hydration.WarningsJson,
            ApiMetadataJson = hydration.ApiMetadataJson,
            LastHydratedAt = hydration.LastHydratedAt,
            CreatedAt = hydration.CreatedAt,
            UpdatedAt = now,
        };
    }

    public static SeriesManifestItemRecord WithOwnership(
        this SeriesManifestItemRecord item,
        Guid collectionId,
        string ownershipState,
        Guid? linkedWorkId)
    {
        return new SeriesManifestItemRecord
        {
            Id = item.Id,
            CollectionId = collectionId,
            SeriesQid = item.SeriesQid,
            ItemQid = item.ItemQid,
            ItemLabel = item.ItemLabel,
            ItemDescription = item.ItemDescription,
            MediaType = item.MediaType,
            RawOrdinal = item.RawOrdinal,
            ParsedOrdinal = item.ParsedOrdinal,
            SortOrder = item.SortOrder,
            PublicationDate = item.PublicationDate,
            PreviousQid = item.PreviousQid,
            NextQid = item.NextQid,
            ParentCollectionQid = item.ParentCollectionQid,
            ParentCollectionLabel = item.ParentCollectionLabel,
            IsCollection = item.IsCollection,
            IsExpandedFromCollection = item.IsExpandedFromCollection,
            SourcePropertiesJson = item.SourcePropertiesJson,
            RelationshipsJson = item.RelationshipsJson,
            OrderSource = item.OrderSource,
            OwnershipState = ownershipState,
            LinkedWorkId = linkedWorkId,
            LastHydratedAt = item.LastHydratedAt,
            CreatedAt = item.CreatedAt,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
    }
}
