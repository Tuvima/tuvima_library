using System.Text.Json;
using System.Text.Json.Serialization;
using MediaEngine.Domain;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace MediaEngine.Storage.Services;

/// <summary>
/// Adds catalog rows for child entities Wikidata knows about but the library
/// does not yet own, and stamps Wikidata child metadata onto owned children.
/// </summary>
public sealed class CatalogUpsertService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly IWorkRepository _works;
    private readonly ILogger<CatalogUpsertService>? _logger;
    private readonly IMetadataClaimRepository? _claims;
    private readonly ICanonicalValueRepository? _canonicals;

    public CatalogUpsertService(
        IWorkRepository works,
        ILogger<CatalogUpsertService>? logger = null,
        IMetadataClaimRepository? claims = null,
        ICanonicalValueRepository? canonicals = null)
    {
        ArgumentNullException.ThrowIfNull(works);
        _works = works;
        _logger = logger;
        _claims = claims;
        _canonicals = canonicals;
    }

    /// <summary>
    /// Reads a child_entities_json payload and inserts a catalog Work for every
    /// missing child under <paramref name="parentWorkId"/>. Existing owned or
    /// catalog children are updated with external IDs and child-level metadata.
    /// </summary>
    /// <returns>The number of new catalog rows inserted.</returns>
    public async Task<int> UpsertChildrenAsync(
        Guid parentWorkId,
        MediaType childMediaType,
        string childEntitiesJson,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(childEntitiesJson)) return 0;

        ChildEntityPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<ChildEntityPayload>(childEntitiesJson, JsonOptions);
        }
        catch (JsonException ex)
        {
            _logger?.LogWarning(ex,
                "CatalogUpsert: malformed child_entities_json for parent {ParentWorkId}",
                parentWorkId);
            return 0;
        }

        if (payload is null) return 0;

        var children = childMediaType switch
        {
            MediaType.Music => payload.Tracks,
            MediaType.TV => payload.GetTvEpisodes(),
            MediaType.Comics => payload.Issues,
            _ => null,
        };

        if (children is null || children.Count == 0)
            return 0;

        var inserted = 0;
        foreach (var child in children)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(child.Title)) continue;

            Guid? existing = null;
            if (child.Ordinal is { } ordinal)
                existing = await _works.FindChildByOrdinalAsync(parentWorkId, ordinal, ct);

            existing ??= await _works.FindChildByTitleAsync(parentWorkId, child.Title, ct);

            var ids = BuildExternalIds(child);
            if (existing is not null)
            {
                if (ids is not null)
                    await _works.WriteExternalIdentifiersAsync(existing.Value, ids, ct);

                await PersistChildMetadataAsync(existing.Value, childMediaType, child, ct);
                continue;
            }

            var childWorkId = await _works.InsertCatalogChildAsync(
                childMediaType,
                parentWorkId,
                child.Ordinal,
                ids,
                ct);

            await PersistChildMetadataAsync(childWorkId, childMediaType, child, ct);
            inserted++;
        }

        if (inserted > 0)
        {
            _logger?.LogInformation(
                "CatalogUpsert: added {Count} catalog {MediaType} child(ren) under parent {ParentWorkId}",
                inserted, childMediaType, parentWorkId);
        }

        return inserted;
    }

    private static IReadOnlyDictionary<string, string>? BuildExternalIds(ChildEntity child)
    {
        var ids = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(child.Qid)) ids[BridgeIdKeys.WikidataQid] = child.Qid;
        if (!string.IsNullOrWhiteSpace(child.ImdbId)) ids[BridgeIdKeys.ImdbId] = child.ImdbId;
        if (!string.IsNullOrWhiteSpace(child.TmdbId)) ids[BridgeIdKeys.TmdbId] = child.TmdbId;
        return ids.Count == 0 ? null : ids;
    }

    private async Task PersistChildMetadataAsync(
        Guid childWorkId,
        MediaType childMediaType,
        ChildEntity child,
        CancellationToken ct)
    {
        if (_claims is null && _canonicals is null)
            return;

        var fields = BuildMetadataFields(childMediaType, child);
        if (fields.Count == 0)
            return;

        var now = DateTimeOffset.UtcNow;

        if (_claims is not null)
        {
            var claims = fields
                .Select(field => new MetadataClaim
                {
                    Id = Guid.NewGuid(),
                    EntityId = childWorkId,
                    ProviderId = WellKnownProviders.Wikidata,
                    ClaimKey = field.Key,
                    ClaimValue = field.Value,
                    Confidence = 1.0,
                    ClaimedAt = now,
                })
                .ToList();

            await _claims.InsertBatchAsync(claims, ct);
        }

        if (_canonicals is not null)
        {
            var canonicals = fields
                .Select(field => new CanonicalValue
                {
                    EntityId = childWorkId,
                    Key = field.Key,
                    Value = field.Value,
                    LastScoredAt = now,
                    WinningProviderId = WellKnownProviders.Wikidata,
                })
                .ToList();

            await _canonicals.UpsertBatchAsync(canonicals, ct);
        }
    }

    private static IReadOnlyDictionary<string, string> BuildMetadataFields(
        MediaType childMediaType,
        ChildEntity child)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        Add(fields, BridgeIdKeys.WikidataQid, child.Qid);
        Add(fields, BridgeIdKeys.ImdbId, child.ImdbId);
        Add(fields, BridgeIdKeys.TmdbId, child.TmdbId);
        Add(fields, MetadataFieldConstants.Title, child.Title);
        Add(fields, MetadataFieldConstants.ShortDescription, child.ShortDescription);
        Add(fields, MetadataFieldConstants.Year, ExtractYear(child.ReleaseDate ?? child.AirDate ?? child.PublicationDate));

        if (child.DurationMinutes is { } duration)
            Add(fields, MetadataFieldConstants.Runtime, duration.ToString());

        Add(fields, MetadataFieldConstants.Director, child.Director);

        switch (childMediaType)
        {
            case MediaType.TV:
                Add(fields, MetadataFieldConstants.EpisodeTitle, child.Title);
                Add(fields, MetadataFieldConstants.EpisodeDescription, child.Description);
                if (child.SeasonNumber is { } seasonNumber)
                    Add(fields, MetadataFieldConstants.SeasonNumber, seasonNumber.ToString());
                if (child.EpisodeNumber is { } episodeNumber)
                    Add(fields, MetadataFieldConstants.EpisodeNumber, episodeNumber.ToString());
                break;

            case MediaType.Music:
                if (child.Ordinal is { } trackNumber)
                    Add(fields, MetadataFieldConstants.TrackNumber, trackNumber.ToString());
                break;

            case MediaType.Comics:
                if (child.Ordinal is { } issueNumber)
                    Add(fields, MetadataFieldConstants.SeriesPosition, issueNumber.ToString());
                break;
        }

        return fields;
    }

    private static void Add(IDictionary<string, string> fields, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            fields[key] = value.Trim();
    }

    private static string? ExtractYear(string? date)
    {
        if (string.IsNullOrWhiteSpace(date) || date.Length < 4)
            return null;

        var year = date[..4];
        return year.All(char.IsDigit) ? year : null;
    }

    private sealed class ChildEntityPayload
    {
        [JsonPropertyName("tracks")]
        public List<ChildEntity>? Tracks { get; set; }

        [JsonPropertyName("episodes")]
        public List<ChildEntity>? Episodes { get; set; }

        [JsonPropertyName("seasons")]
        public List<SeasonEntity>? Seasons { get; set; }

        [JsonPropertyName("issues")]
        public List<ChildEntity>? Issues { get; set; }

        public List<ChildEntity>? GetTvEpisodes()
        {
            if (Episodes is { Count: > 0 })
                return Episodes;

            if (Seasons is not { Count: > 0 })
                return null;

            var result = new List<ChildEntity>();
            foreach (var season in Seasons)
            {
                if (season.Episodes is not { Count: > 0 })
                    continue;

                foreach (var episode in season.Episodes)
                {
                    episode.SeasonNumber ??= season.SeasonNumber ?? season.Ordinal;
                    result.Add(episode);
                }
            }

            return result;
        }
    }

    private sealed class SeasonEntity
    {
        [JsonPropertyName("ordinal")]
        public int? Ordinal { get; set; }

        [JsonPropertyName("season_number")]
        public int? SeasonNumber { get; set; }

        [JsonPropertyName("episodes")]
        public List<ChildEntity>? Episodes { get; set; }
    }

    private sealed class ChildEntity
    {
        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("ordinal")]
        public int? Ordinal { get; set; }

        [JsonPropertyName("qid")]
        public string? Qid { get; set; }

        [JsonPropertyName("imdb_id")]
        public string? ImdbId { get; set; }

        [JsonPropertyName("tmdb_id")]
        public string? TmdbId { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("short_description")]
        public string? ShortDescription { get; set; }

        [JsonPropertyName("air_date")]
        public string? AirDate { get; set; }

        [JsonPropertyName("release_date")]
        public string? ReleaseDate { get; set; }

        [JsonPropertyName("publication_date")]
        public string? PublicationDate { get; set; }

        [JsonPropertyName("duration_minutes")]
        public int? DurationMinutes { get; set; }

        [JsonPropertyName("director")]
        public string? Director { get; set; }

        [JsonPropertyName("season_number")]
        public int? SeasonNumber { get; set; }

        [JsonPropertyName("episode_number")]
        public int? EpisodeNumber { get; set; }
    }
}
