using Dapper;
using MediaEngine.Api.Models;
using MediaEngine.Api.Security;
using MediaEngine.Domain.Contracts;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Api.Endpoints;

public static class WorkEndpoints
{
    public static IEndpointRouteBuilder MapWorkEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/works")
                       .WithTags("Works");

        group.MapGet("/{workId:guid}", async (
            Guid workId,
            IDatabaseConnection db,
            CancellationToken ct) =>
        {
            var detail = await LoadWorkDetailAsync(workId, db, includeEditions: true, ct);
            return detail is null ? Results.NotFound() : Results.Ok(detail);
        })
        .WithName("GetWorkDetail")
        .WithSummary("Returns a single work with canonical values, editions, and owned assets.")
        .Produces<WorkDetailDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAnyRole();

        group.MapGet("/{workId:guid}/editions", async (
            Guid workId,
            IDatabaseConnection db,
            CancellationToken ct) =>
        {
            var detail = await LoadWorkDetailAsync(workId, db, includeEditions: true, ct);
            return detail is null ? Results.NotFound() : Results.Ok(detail.Editions);
        })
        .WithName("GetWorkEditions")
        .WithSummary("Returns editions and owned assets for a single work.")
        .Produces<List<EditionDto>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAnyRole();

        group.MapGet("/{workId:guid}/cast", async (
            Guid workId,
            ICanonicalValueArrayRepository canonicalArrayRepo,
            IPersonRepository personRepo,
            IDatabaseConnection db,
            CancellationToken ct) =>
        {
            var cast = await CastCreditQueries.BuildForWorkAsync(
                workId,
                canonicalArrayRepo,
                personRepo,
                db,
                ct);

            return Results.Ok(cast);
        })
        .WithName("GetWorkCast")
        .WithSummary("Returns actor and character credits for a single work.")
        .Produces<List<CastCreditDto>>(StatusCodes.Status200OK)
        .RequireAnyRole();

        return app;
    }

    private static async Task<WorkDetailDto?> LoadWorkDetailAsync(
        Guid workId,
        IDatabaseConnection db,
        bool includeEditions,
        CancellationToken ct)
    {
        using var conn = db.CreateConnection();

        var row = await conn.QuerySingleOrDefaultAsync<WorkDetailRow>(new CommandDefinition("""
            SELECT id AS Id,
                   collection_id AS CollectionId,
                   parent_work_id AS ParentWorkId,
                   media_type AS MediaType,
                   work_kind AS WorkKind,
                   ordinal AS Ordinal,
                   is_catalog_only AS IsCatalogOnly,
                   wikidata_qid AS WikidataQid
            FROM works
            WHERE id = @workId
            LIMIT 1;
            """, new { workId = workId.ToString() }, cancellationToken: ct));

        if (row is null)
        {
            return null;
        }

        var work = new WorkDetailDto
        {
            Id = row.Id,
            CollectionId = row.CollectionId,
            ParentWorkId = row.ParentWorkId,
            MediaType = row.MediaType,
            WorkKind = row.WorkKind,
            Ordinal = row.Ordinal,
            IsCatalogOnly = row.IsCatalogOnly,
            WikidataQid = row.WikidataQid,
            CanonicalValues = await LoadCanonicalValuesAsync(conn, [workId], ct),
        };

        if (!includeEditions)
        {
            return work;
        }

        var editionRows = (await conn.QueryAsync<EditionRow>(new CommandDefinition("""
            SELECT id AS Id,
                   work_id AS WorkId,
                   format_label AS FormatLabel,
                   wikidata_qid AS WikidataQid
            FROM editions
            WHERE work_id = @workId
            ORDER BY COALESCE(format_label, ''), id;
            """, new { workId = workId.ToString() }, cancellationToken: ct))).ToList();

        List<EditionAssetRow> assetRows = editionRows.Count == 0
            ? []
            : (await conn.QueryAsync<EditionAssetRow>(new CommandDefinition("""
                SELECT id AS Id,
                       edition_id AS EditionId,
                       file_path_root AS FilePathRoot,
                       status AS Status
                FROM media_assets
                WHERE edition_id IN @editionIds
                  AND status = 'Normal'
                ORDER BY file_path_root;
                """, new { editionIds = editionRows.Select(e => e.Id.ToString()).ToArray() }, cancellationToken: ct))).ToList();

        var canonicalEntityIds = editionRows.Select(e => e.Id)
            .Concat(assetRows.Select(a => a.Id))
            .Distinct()
            .ToArray();
        var canonicalByEntity = await LoadCanonicalValuesByEntityAsync(conn, canonicalEntityIds, ct);
        var assetsByEdition = assetRows
            .GroupBy(asset => asset.EditionId)
            .ToDictionary(group => group.Key, group => group.Select(asset => new EditionAssetDto
            {
                Id = asset.Id,
                EditionId = asset.EditionId,
                FilePathRoot = asset.FilePathRoot,
                Status = asset.Status,
                CanonicalValues = canonicalByEntity.GetValueOrDefault(asset.Id) ?? [],
            }).ToList());

        work.Editions.AddRange(editionRows.Select(edition => new EditionDto
        {
            Id = edition.Id,
            WorkId = edition.WorkId,
            FormatLabel = edition.FormatLabel,
            WikidataQid = edition.WikidataQid,
            CanonicalValues = canonicalByEntity.GetValueOrDefault(edition.Id) ?? [],
            Assets = assetsByEdition.GetValueOrDefault(edition.Id) ?? [],
        }));

        return work;
    }

    private static async Task<List<CanonicalValueDto>> LoadCanonicalValuesAsync(
        System.Data.IDbConnection conn,
        IReadOnlyCollection<Guid> entityIds,
        CancellationToken ct)
    {
        var values = await LoadCanonicalValuesByEntityAsync(conn, entityIds, ct);
        return values.SelectMany(kvp => kvp.Value).ToList();
    }

    private static async Task<Dictionary<Guid, List<CanonicalValueDto>>> LoadCanonicalValuesByEntityAsync(
        System.Data.IDbConnection conn,
        IReadOnlyCollection<Guid> entityIds,
        CancellationToken ct)
    {
        if (entityIds.Count == 0)
        {
            return [];
        }

        var rows = await conn.QueryAsync<CanonicalValueRow>(new CommandDefinition("""
            SELECT entity_id AS EntityId,
                   key AS Key,
                   value AS Value,
                   last_scored_at AS LastScoredAt
            FROM canonical_values
            WHERE entity_id IN @entityIds
            ORDER BY key;
            """, new { entityIds = entityIds.Select(id => id.ToString()).ToArray() }, cancellationToken: ct));

        return rows
            .GroupBy(row => row.EntityId)
            .ToDictionary(
                group => group.Key,
                group => group.Select(row => new CanonicalValueDto
                {
                    Key = row.Key,
                    Value = row.Value,
                    LastScoredAt = row.LastScoredAt,
                }).ToList());
    }

    private sealed class WorkDetailRow
    {
        public Guid Id { get; init; }
        public Guid? CollectionId { get; init; }
        public Guid? ParentWorkId { get; init; }
        public string MediaType { get; init; } = string.Empty;
        public string WorkKind { get; init; } = string.Empty;
        public int? Ordinal { get; init; }
        public bool IsCatalogOnly { get; init; }
        public string? WikidataQid { get; init; }
    }

    private sealed class EditionRow
    {
        public Guid Id { get; init; }
        public Guid WorkId { get; init; }
        public string? FormatLabel { get; init; }
        public string? WikidataQid { get; init; }
    }

    private sealed class EditionAssetRow
    {
        public Guid Id { get; init; }
        public Guid EditionId { get; init; }
        public string FilePathRoot { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
    }

    private sealed class CanonicalValueRow
    {
        public Guid EntityId { get; init; }
        public string Key { get; init; } = string.Empty;
        public string Value { get; init; } = string.Empty;
        public DateTimeOffset LastScoredAt { get; init; }
    }
}
