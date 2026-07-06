using System.Text.Json;
using Dapper;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Models;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Storage;

public sealed class SeriesManifestRepository : ISeriesManifestRepository
{
    private readonly IDatabaseConnection _db;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public SeriesManifestRepository(IDatabaseConnection db)
    {
        ArgumentNullException.ThrowIfNull(db);
        _db = db;
    }

    public Task<SeriesManifestHydration?> GetHydrationAsync(string seriesQid, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        var row = conn.QueryFirstOrDefault<HydrationRow>(
            """
            SELECT series_qid AS SeriesQid, collection_id AS CollectionId, series_label AS SeriesLabel,
                   manifest_source AS ManifestSource, manifest_version AS ManifestVersion,
                   manifest_hash AS ManifestHash, known_item_qids_hash AS KnownItemQidsHash,
                   warnings_json AS WarningsJson, api_metadata_json AS ApiMetadataJson,
                   last_hydrated_at AS LastHydratedAt, created_at AS CreatedAt, updated_at AS UpdatedAt
            FROM series_manifest_hydrations
            WHERE series_qid = @seriesQid
            LIMIT 1;
            """,
            new { seriesQid });

        return Task.FromResult(row?.ToEntity());
    }

    public Task<IReadOnlyList<SeriesManifestItemRecord>> GetItemsBySeriesQidAsync(
        string seriesQid,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        var rows = conn.Query<ItemRow>(
            """
            SELECT id AS Id, collection_id AS CollectionId, series_qid AS SeriesQid,
                   item_qid AS ItemQid, item_label AS ItemLabel, item_description AS ItemDescription,
                   media_type AS MediaType, raw_ordinal AS RawOrdinal, parsed_ordinal AS ParsedOrdinal,
                   sort_order AS SortOrder, publication_date AS PublicationDate,
                   previous_qid AS PreviousQid, next_qid AS NextQid,
                   parent_collection_qid AS ParentCollectionQid,
                   parent_collection_label AS ParentCollectionLabel,
                   is_collection AS IsCollection, is_expanded_from_collection AS IsExpandedFromCollection,
                   source_properties_json AS SourcePropertiesJson,
                   relationships_json AS RelationshipsJson, order_source AS OrderSource,
                   ownership_state AS OwnershipState, linked_work_id AS LinkedWorkId,
                   last_hydrated_at AS LastHydratedAt, created_at AS CreatedAt, updated_at AS UpdatedAt
            FROM series_manifest_items
            WHERE series_qid = @seriesQid
            ORDER BY COALESCE(sort_order, 999999), COALESCE(item_label, item_qid), item_qid;
            """,
            new { seriesQid }).AsList();

        return Task.FromResult<IReadOnlyList<SeriesManifestItemRecord>>(rows.Select(r => r.ToEntity()).ToList());
    }

    public Task<IReadOnlyDictionary<string, IReadOnlyList<Guid>>> FindWorkIdsByQidsAsync(
        IReadOnlyCollection<string> qids,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (qids.Count == 0)
            return Task.FromResult<IReadOnlyDictionary<string, IReadOnlyList<Guid>>>(
                new Dictionary<string, IReadOnlyList<Guid>>(StringComparer.OrdinalIgnoreCase));

        using var conn = _db.CreateConnection();
        var rows = conn.Query<WorkQidRow>(
            """
            SELECT id AS WorkId,
                   CAST(COALESCE(NULLIF(wikidata_qid, ''), json_extract(external_identifiers, '$.wikidata_qid')) AS TEXT) AS Qid
            FROM works
            WHERE COALESCE(NULLIF(wikidata_qid, ''), json_extract(external_identifiers, '$.wikidata_qid')) IN @qids;
            """,
            new { qids = qids.Distinct(StringComparer.OrdinalIgnoreCase).ToArray() }).AsList();

        var result = rows
            .Where(r => !string.IsNullOrWhiteSpace(r.Qid))
            .GroupBy(r => r.Qid!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<Guid>)g.Select(r => r.WorkId).Distinct().ToList(),
                StringComparer.OrdinalIgnoreCase);

        return Task.FromResult<IReadOnlyDictionary<string, IReadOnlyList<Guid>>>(result);
    }

    public async Task UpsertManifestAsync(
        SeriesManifestHydration hydration,
        IReadOnlyList<SeriesManifestItemRecord> items,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await _db.AcquireWriteLockAsync(ct);
        try
        {
            using var conn = _db.CreateConnection();
            using var tx = conn.BeginTransaction();

            conn.Execute(
                """
                INSERT INTO series_manifest_hydrations
                    (series_qid, collection_id, series_label, manifest_source, manifest_version,
                     manifest_hash, known_item_qids_hash, warnings_json, api_metadata_json,
                     last_hydrated_at, created_at, updated_at)
                VALUES
                    (@SeriesQid, @CollectionId, @SeriesLabel, @ManifestSource, @ManifestVersion,
                     @ManifestHash, @KnownItemQidsHash, @WarningsJson, @ApiMetadataJson,
                     @LastHydratedAt, @CreatedAt, @UpdatedAt)
                ON CONFLICT(series_qid) DO UPDATE SET
                    collection_id = excluded.collection_id,
                    series_label = excluded.series_label,
                    manifest_source = excluded.manifest_source,
                    manifest_version = excluded.manifest_version,
                    manifest_hash = excluded.manifest_hash,
                    known_item_qids_hash = excluded.known_item_qids_hash,
                    warnings_json = excluded.warnings_json,
                    api_metadata_json = excluded.api_metadata_json,
                    last_hydrated_at = excluded.last_hydrated_at,
                    updated_at = excluded.updated_at;
                """,
                ToHydrationParams(hydration),
                tx);

            foreach (var item in items)
            {
                conn.Execute(
                    """
                    INSERT INTO series_manifest_items
                        (id, collection_id, series_qid, item_qid, item_label, item_description,
                         media_type, raw_ordinal, parsed_ordinal, sort_order, publication_date,
                         previous_qid, next_qid, parent_collection_qid, parent_collection_label,
                         is_collection, is_expanded_from_collection, source_properties_json,
                         relationships_json, order_source, ownership_state, linked_work_id,
                         last_hydrated_at, created_at, updated_at)
                    VALUES
                        (@Id, @CollectionId, @SeriesQid, @ItemQid, @ItemLabel, @ItemDescription,
                         @MediaType, @RawOrdinal, @ParsedOrdinal, @SortOrder, @PublicationDate,
                         @PreviousQid, @NextQid, @ParentCollectionQid, @ParentCollectionLabel,
                         @IsCollection, @IsExpandedFromCollection, @SourcePropertiesJson,
                         @RelationshipsJson, @OrderSource, @OwnershipState, @LinkedWorkId,
                         @LastHydratedAt, @CreatedAt, @UpdatedAt)
                    ON CONFLICT(collection_id, item_qid) DO UPDATE SET
                        series_qid = excluded.series_qid,
                        item_label = excluded.item_label,
                        item_description = excluded.item_description,
                        media_type = excluded.media_type,
                        raw_ordinal = excluded.raw_ordinal,
                        parsed_ordinal = excluded.parsed_ordinal,
                        sort_order = excluded.sort_order,
                        publication_date = excluded.publication_date,
                        previous_qid = excluded.previous_qid,
                        next_qid = excluded.next_qid,
                        parent_collection_qid = excluded.parent_collection_qid,
                        parent_collection_label = excluded.parent_collection_label,
                        is_collection = excluded.is_collection,
                        is_expanded_from_collection = excluded.is_expanded_from_collection,
                        source_properties_json = excluded.source_properties_json,
                        relationships_json = excluded.relationships_json,
                        order_source = excluded.order_source,
                        ownership_state = excluded.ownership_state,
                        linked_work_id = excluded.linked_work_id,
                        last_hydrated_at = excluded.last_hydrated_at,
                        updated_at = excluded.updated_at;
                    """,
                    ToItemParams(item),
                    tx);
            }

            tx.Commit();
        }
        finally
        {
            _db.ReleaseWriteLock();
        }
    }

    public async Task LinkOwnedWorksAsync(
        Guid collectionId,
        IReadOnlyList<SeriesManifestItemRecord> items,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await _db.AcquireWriteLockAsync(ct);
        try
        {
            using var conn = _db.CreateConnection();
            using var tx = conn.BeginTransaction();

            foreach (var item in items.Where(i => i.OwnershipState == "Owned"))
            {
                if (item.LinkedWorkId is not { } workId)
                {
                    continue;
                }

                var sortOrder = item.SortOrder.HasValue
                    ? (int)Math.Round(item.SortOrder.Value, MidpointRounding.AwayFromZero)
                    : 0;

                conn.Execute(
                    """
                    INSERT INTO collection_items
                        (id, collection_id, work_id, sort_order, progress_state, added_at)
                    SELECT @id, @collectionId, @workId, @sortOrder, 'not_started', @addedAt
                    WHERE NOT EXISTS (
                        SELECT 1 FROM collection_items
                        WHERE collection_id = @collectionId AND work_id = @workId
                    );

                    UPDATE collection_items
                    SET sort_order = @sortOrder
                    WHERE collection_id = @collectionId AND work_id = @workId;

                    UPDATE works
                    SET collection_id = @collectionId
                    WHERE id = @workId
                      AND (collection_id IS NULL OR collection_id = @collectionId);
                    """,
                    new
                    {
                        id = Guid.NewGuid(),
                        collectionId,
                        workId,
                        sortOrder,
                        addedAt = DateTimeOffset.UtcNow.ToString("O"),
                    },
                    tx);
            }

            tx.Commit();
        }
        finally
        {
            _db.ReleaseWriteLock();
        }
    }

    public Task<SeriesManifestViewDto?> GetViewByCollectionIdAsync(Guid collectionId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        var hydration = conn.QueryFirstOrDefault<HydrationRow>(
            """
            SELECT series_qid AS SeriesQid, collection_id AS CollectionId, series_label AS SeriesLabel,
                   manifest_source AS ManifestSource, manifest_version AS ManifestVersion,
                   manifest_hash AS ManifestHash, known_item_qids_hash AS KnownItemQidsHash,
                   warnings_json AS WarningsJson, api_metadata_json AS ApiMetadataJson,
                   last_hydrated_at AS LastHydratedAt, created_at AS CreatedAt, updated_at AS UpdatedAt
            FROM series_manifest_hydrations
            WHERE collection_id = @collectionId
            LIMIT 1;
            """,
            new { collectionId });

        if (hydration is null)
            return Task.FromResult<SeriesManifestViewDto?>(null);

        var rows = conn.Query<ItemRow>(
            """
            SELECT id AS Id, collection_id AS CollectionId, series_qid AS SeriesQid,
                   item_qid AS ItemQid, item_label AS ItemLabel, item_description AS ItemDescription,
                   media_type AS MediaType, raw_ordinal AS RawOrdinal, parsed_ordinal AS ParsedOrdinal,
                   sort_order AS SortOrder, publication_date AS PublicationDate,
                   previous_qid AS PreviousQid, next_qid AS NextQid,
                   parent_collection_qid AS ParentCollectionQid,
                   parent_collection_label AS ParentCollectionLabel,
                   is_collection AS IsCollection, is_expanded_from_collection AS IsExpandedFromCollection,
                   source_properties_json AS SourcePropertiesJson,
                   relationships_json AS RelationshipsJson, order_source AS OrderSource,
                   ownership_state AS OwnershipState, linked_work_id AS LinkedWorkId,
                   last_hydrated_at AS LastHydratedAt, created_at AS CreatedAt, updated_at AS UpdatedAt
            FROM series_manifest_items
            WHERE collection_id = @collectionId
            ORDER BY COALESCE(sort_order, 999999), COALESCE(item_label, item_qid), item_qid;
            """,
            new { collectionId }).AsList();

        var items = rows.Select(r => r.ToDto()).ToList();
        var warnings = DeserializeWarnings(hydration.WarningsJson);
        var metadata = DeserializeApiMetadata(hydration.ApiMetadataJson);
        var ownedCount = items.Count(i => i.OwnershipState == "Owned");
        var provisionalCount = items.Count(i => i.OwnershipState == "Provisional");
        var ambiguousCount = items.Count(i => i.OwnershipState == "Ambiguous");
        var rowMissingCount = items.Count(i => i.OwnershipState == "Missing");
        var totalCount = Math.Max(items.Count, metadata.ExpectedTotal ?? 0);
        var expectedMissingCount = Math.Max(0, totalCount - ownedCount - provisionalCount - ambiguousCount);
        var missingCount = Math.Max(rowMissingCount, expectedMissingCount);

        return Task.FromResult<SeriesManifestViewDto?>(new SeriesManifestViewDto
        {
            CollectionId = hydration.CollectionId,
            SeriesQid = hydration.SeriesQid,
            SeriesLabel = hydration.SeriesLabel,
            LastHydratedAt = DateTimeOffset.Parse(hydration.LastHydratedAt),
            ContainerKind = metadata.ContainerKind,
            ExpectedTotal = metadata.ExpectedTotal,
            ExpectedTotalKind = metadata.ExpectedTotalKind,
            ExpectedTotalSource = metadata.ExpectedTotalSource,
            ExpectedTotalConfidence = metadata.ExpectedTotalConfidence,
            TotalCount = totalCount,
            OwnedCount = ownedCount,
            MissingCount = missingCount,
            ProvisionalCount = provisionalCount,
            AmbiguousCount = ambiguousCount,
            Warnings = warnings,
            Items = items,
        });
    }

    private static object ToHydrationParams(SeriesManifestHydration hydration)
        => new
        {
            hydration.SeriesQid,
            hydration.CollectionId,
            hydration.SeriesLabel,
            hydration.ManifestSource,
            hydration.ManifestVersion,
            hydration.ManifestHash,
            hydration.KnownItemQidsHash,
            hydration.WarningsJson,
            hydration.ApiMetadataJson,
            LastHydratedAt = hydration.LastHydratedAt.ToString("O"),
            CreatedAt = hydration.CreatedAt.ToString("O"),
            UpdatedAt = hydration.UpdatedAt.ToString("O"),
        };

    private static object ToItemParams(SeriesManifestItemRecord item)
        => new
        {
            Id = item.Id == Guid.Empty ? Guid.NewGuid() : item.Id,
            item.CollectionId,
            item.SeriesQid,
            item.ItemQid,
            item.ItemLabel,
            item.ItemDescription,
            item.MediaType,
            item.RawOrdinal,
            item.ParsedOrdinal,
            item.SortOrder,
            item.PublicationDate,
            item.PreviousQid,
            item.NextQid,
            item.ParentCollectionQid,
            item.ParentCollectionLabel,
            IsCollection = item.IsCollection ? 1 : 0,
            IsExpandedFromCollection = item.IsExpandedFromCollection ? 1 : 0,
            item.SourcePropertiesJson,
            item.RelationshipsJson,
            item.OrderSource,
            item.OwnershipState,
            item.LinkedWorkId,
            LastHydratedAt = item.LastHydratedAt.ToString("O"),
            CreatedAt = item.CreatedAt.ToString("O"),
            UpdatedAt = item.UpdatedAt.ToString("O"),
        };

    private static IReadOnlyList<SeriesManifestWarningDto> DeserializeWarnings(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];

        try
        {
            return JsonSerializer.Deserialize<IReadOnlyList<SeriesManifestWarningDto>>(json, JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static ManifestApiMetadata DeserializeApiMetadata(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new ManifestApiMetadata();

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            return new ManifestApiMetadata
            {
                ContainerKind = ReadString(root, "containerKind", "container_kind"),
                ExpectedTotal = ReadInt(root, "expectedTotal", "expected_total"),
                ExpectedTotalKind = ReadString(root, "expectedTotalKind", "expected_total_kind"),
                ExpectedTotalSource = ReadString(root, "expectedTotalSource", "expected_total_source"),
                ExpectedTotalConfidence = ReadDouble(root, "expectedTotalConfidence", "expected_total_confidence"),
            };
        }
        catch (JsonException)
        {
            // Metadata is diagnostic/cache state; row-level manifest data remains valid.
            return new ManifestApiMetadata();
        }
    }

    private static string? ReadString(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
                return value.GetString();
        }

        return null;
    }

    private static int? ReadInt(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (!root.TryGetProperty(name, out var value))
                continue;

            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var parsed))
                return parsed;

            if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out parsed))
                return parsed;
        }

        return null;
    }

    private static double? ReadDouble(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (!root.TryGetProperty(name, out var value))
                continue;

            if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var parsed))
                return parsed;

            if (value.ValueKind == JsonValueKind.String && double.TryParse(value.GetString(), out parsed))
                return parsed;
        }

        return null;
    }

    private sealed class ManifestApiMetadata
    {
        public string? ContainerKind { get; init; }
        public int? ExpectedTotal { get; init; }
        public string? ExpectedTotalKind { get; init; }
        public string? ExpectedTotalSource { get; init; }
        public double? ExpectedTotalConfidence { get; init; }
    }

    private sealed class HydrationRow
    {
        public required string SeriesQid { get; init; }
        public required Guid CollectionId { get; init; }
        public string? SeriesLabel { get; init; }
        public string ManifestSource { get; init; } = "Tuvima.Wikidata";
        public string? ManifestVersion { get; init; }
        public string? ManifestHash { get; init; }
        public string? KnownItemQidsHash { get; init; }
        public string WarningsJson { get; init; } = "[]";
        public string ApiMetadataJson { get; init; } = "{}";
        public required string LastHydratedAt { get; init; }
        public required string CreatedAt { get; init; }
        public required string UpdatedAt { get; init; }

        public SeriesManifestHydration ToEntity() => new()
        {
            SeriesQid = SeriesQid,
            CollectionId = CollectionId,
            SeriesLabel = SeriesLabel,
            ManifestSource = ManifestSource,
            ManifestVersion = ManifestVersion,
            ManifestHash = ManifestHash,
            KnownItemQidsHash = KnownItemQidsHash,
            WarningsJson = WarningsJson,
            ApiMetadataJson = ApiMetadataJson,
            LastHydratedAt = DateTimeOffset.Parse(LastHydratedAt),
            CreatedAt = DateTimeOffset.Parse(CreatedAt),
            UpdatedAt = DateTimeOffset.Parse(UpdatedAt),
        };
    }

    private sealed class ItemRow
    {
        public required Guid Id { get; init; }
        public required Guid CollectionId { get; init; }
        public required string SeriesQid { get; init; }
        public required string ItemQid { get; init; }
        public string? ItemLabel { get; init; }
        public string? ItemDescription { get; init; }
        public string? MediaType { get; init; }
        public string? RawOrdinal { get; init; }
        public double? ParsedOrdinal { get; init; }
        public double? SortOrder { get; init; }
        public string? PublicationDate { get; init; }
        public string? PreviousQid { get; init; }
        public string? NextQid { get; init; }
        public string? ParentCollectionQid { get; init; }
        public string? ParentCollectionLabel { get; init; }
        public int IsCollection { get; init; }
        public int IsExpandedFromCollection { get; init; }
        public string SourcePropertiesJson { get; init; } = "[]";
        public string RelationshipsJson { get; init; } = "[]";
        public string OrderSource { get; init; } = "Unknown";
        public string OwnershipState { get; init; } = "Missing";
        public Guid? LinkedWorkId { get; init; }
        public required string LastHydratedAt { get; init; }
        public required string CreatedAt { get; init; }
        public required string UpdatedAt { get; init; }

        public SeriesManifestItemRecord ToEntity() => new()
        {
            Id = Id,
            CollectionId = CollectionId,
            SeriesQid = SeriesQid,
            ItemQid = ItemQid,
            ItemLabel = ItemLabel,
            ItemDescription = ItemDescription,
            MediaType = MediaType,
            RawOrdinal = RawOrdinal,
            ParsedOrdinal = ParsedOrdinal,
            SortOrder = SortOrder,
            PublicationDate = PublicationDate,
            PreviousQid = PreviousQid,
            NextQid = NextQid,
            ParentCollectionQid = ParentCollectionQid,
            ParentCollectionLabel = ParentCollectionLabel,
            IsCollection = IsCollection == 1,
            IsExpandedFromCollection = IsExpandedFromCollection == 1,
            SourcePropertiesJson = SourcePropertiesJson,
            RelationshipsJson = RelationshipsJson,
            OrderSource = OrderSource,
            OwnershipState = OwnershipState,
            LinkedWorkId = LinkedWorkId,
            LastHydratedAt = DateTimeOffset.Parse(LastHydratedAt),
            CreatedAt = DateTimeOffset.Parse(CreatedAt),
            UpdatedAt = DateTimeOffset.Parse(UpdatedAt),
        };

        public SeriesManifestItemDto ToDto() => new()
        {
            Id = Id,
            ItemQid = ItemQid,
            ItemLabel = ItemLabel,
            ItemDescription = ItemDescription,
            MediaType = MediaType,
            RawOrdinal = RawOrdinal,
            ParsedOrdinal = ParsedOrdinal,
            SortOrder = SortOrder,
            PublicationDate = PublicationDate,
            ParentCollectionQid = ParentCollectionQid,
            ParentCollectionLabel = ParentCollectionLabel,
            IsCollection = IsCollection == 1,
            IsExpandedFromCollection = IsExpandedFromCollection == 1,
            OrderSource = OrderSource,
            OwnershipState = OwnershipState,
            LinkedWorkId = LinkedWorkId,
        };
    }

    private sealed class WorkQidRow
    {
        public Guid WorkId { get; init; }
        public string? Qid { get; init; }
    }
}
