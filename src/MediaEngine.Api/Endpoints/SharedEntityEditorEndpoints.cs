using MediaEngine.Api.Http;
using MediaEngine.Api.Security;
using MediaEngine.Api.Services.Display;
using MediaEngine.Contracts.Universe;
using MediaEngine.Domain.Authorization;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Models;
using System.Security.Cryptography;
using System.Text;

namespace MediaEngine.Api.Endpoints;

/// <summary>Authorized shared-editor projections for roots and fictional entities. Graph facts are read-only.</summary>
public static class SharedEntityEditorEndpoints
{
    private sealed record Category(string Id, string Label);
    // Ordered client contract; unlike a set this cannot drift between runs.
    private static readonly IReadOnlyList<Category> Categories =
    [ new("Character", "Character"), new("Location", "Places"), new("Organization", "Groups"), new("Event", "Event"), new("Object", "Object") ];

    public static IEndpointRouteBuilder MapSharedEntityEditorEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/entity-editor").WithTags("Shared Entity Editor");
        group.MapGet("/universes/{qid}/context", GetUniverseContextAsync).RequireClientScope(ApplicationPermissionIds.MetadataRead.Value);
        group.MapGet("/universes/{qid}/categories", GetCategorySummariesAsync).RequireClientScope(ApplicationPermissionIds.MetadataRead.Value);
        group.MapGet("/universes/{qid}/entities", GetSelectorAsync).RequireClientScope(ApplicationPermissionIds.MetadataRead.Value);
        group.MapGet("/universes/{qid}/details", GetUniverseDetailsAsync).RequireClientScope(ApplicationPermissionIds.MetadataRead.Value);
        group.MapPut("/universes/{qid}/details", UpdateUniverseDetailsAsync).RequireAdministratorOrApplication(ApplicationPermissionIds.MetadataWrite).RequireClientScope(ApplicationPermissionIds.MetadataWrite.Value);
        group.MapGet("/universes/{qid}/artwork", GetUniverseArtworkAsync).RequireClientScope(ApplicationPermissionIds.ArtworkRead.Value);
        group.MapPut("/universes/{qid}/artwork", UpdateUniverseArtworkAsync).RequireAdministratorOrApplication(ApplicationPermissionIds.MetadataWrite).RequireClientScope(ApplicationPermissionIds.MetadataWrite.Value);
        group.MapGet("/universes/{qid}/history", GetUniverseHistoryAsync).RequireClientScope(ApplicationPermissionIds.MetadataRead.Value);
        group.MapGet("/universes/{qid}/entities/{id:guid}/context", GetEntityContextAsync).RequireClientScope(ApplicationPermissionIds.MetadataRead.Value);
        group.MapGet("/universes/{qid}/entities/{id:guid}/details", GetEntityDetailsAsync).RequireClientScope(ApplicationPermissionIds.MetadataRead.Value);
        group.MapPut("/universes/{qid}/entities/{id:guid}/details", UpdateEntityDetailsAsync).RequireAdministratorOrApplication(ApplicationPermissionIds.MetadataWrite).RequireClientScope(ApplicationPermissionIds.MetadataWrite.Value);
        group.MapGet("/universes/{qid}/entities/{id:guid}/artwork", GetEntityArtworkAsync).RequireClientScope(ApplicationPermissionIds.ArtworkRead.Value);
        group.MapPut("/universes/{qid}/entities/{id:guid}/artwork", UpdateEntityArtworkAsync).RequireAdministratorOrApplication(ApplicationPermissionIds.MetadataWrite).RequireClientScope(ApplicationPermissionIds.MetadataWrite.Value);
        group.MapGet("/universes/{qid}/entities/{id:guid}/appearances", GetAppearancesAsync).RequireClientScope(ApplicationPermissionIds.MetadataRead.Value);
        group.MapGet("/universes/{qid}/entities/{id:guid}/relationships", GetRelationshipsAsync).RequireClientScope(ApplicationPermissionIds.MetadataRead.Value);
        group.MapGet("/universes/{qid}/entities/{id:guid}/timeline", GetTimelineAsync).RequireClientScope(ApplicationPermissionIds.MetadataRead.Value);
        group.MapGet("/universes/{qid}/entities/{id:guid}/sources", GetSourcesAsync).RequireClientScope(ApplicationPermissionIds.MetadataRead.Value);
        group.MapGet("/universes/{qid}/entities/{id:guid}/history", GetHistoryAsync).RequireClientScope(ApplicationPermissionIds.MetadataRead.Value);
        group.MapGet("/universes/{qid}/entities/{id:guid}/enrichment", GetEnrichmentAsync).RequireClientScope(ApplicationPermissionIds.MetadataRead.Value);
        group.MapPost("/universes/{qid}/entities/{id:guid}/refresh", RefreshEntityAsync).RequireAdministratorOrApplication(ApplicationPermissionIds.MetadataEnrichmentRun).RequireClientScope(ApplicationPermissionIds.MetadataEnrichmentRun.Value);
        return app;
    }

    private static async Task<IResult> GetUniverseContextAsync(string qid, HttpContext http, INarrativeRootRepository roots, IFictionalEntityRepository entities, IDisplayProjectionReadService display, CatalogueResourceAuthorizationService authorization, CancellationToken ct)
    {
        var root = await AuthorizedRootAsync(qid, http, roots, entities, display, authorization, ApplicationPermissionIds.MetadataRead, ct);
        return root is null ? ApiErrors.NotFound("Universe not found.") : Results.Ok(new SharedEntityEditorContextDto(new(SharedEntityEditorTargetKinds.Universe, root.Qid, null, root.Qid), root.Label, "Universe", Capabilities(), "available"));
    }

    private static async Task<IResult> GetCategorySummariesAsync(string qid, HttpContext http, INarrativeRootRepository roots, IFictionalEntityRepository entities, IDisplayProjectionReadService display, CatalogueResourceAuthorizationService authorization, CancellationToken ct)
    {
        if (await AuthorizedRootAsync(qid, http, roots, entities, display, authorization, ApplicationPermissionIds.MetadataRead, ct) is null) return ApiErrors.NotFound("Universe not found.");
        var visible = await VisibleWorksAsync(display, ct);
        var items = new List<SharedEntityCategorySummaryDto>(Categories.Count);
        foreach (var category in Categories)
        {
            var result = await entities.SearchVisibleByUniverseAsync(qid, visible, category.Id, null, 0, 1, ct);
            items.Add(new SharedEntityCategorySummaryDto(category.Id, category.Label, result.Total));
        }
        return Results.Ok(items);
    }

    private static async Task<IResult> GetSelectorAsync(string qid, string? category, string? search, int? offset, int? limit, HttpContext http, INarrativeRootRepository roots, IFictionalEntityRepository entities, IDisplayProjectionReadService display, CatalogueResourceAuthorizationService authorization, CancellationToken ct)
    {
        if (await AuthorizedRootAsync(qid, http, roots, entities, display, authorization, ApplicationPermissionIds.MetadataRead, ct) is null) return ApiErrors.NotFound("Universe not found.");
        var selected = ResolveCategory(category);
        if (category is not null && selected is null) return ApiErrors.BadRequest("Unsupported entity category.");
        var skip = Math.Max(0, offset ?? 0);
        var take = Math.Clamp(limit ?? 50, 1, 100);
        var page = await entities.SearchVisibleByUniverseAsync(qid, await VisibleWorksAsync(display, ct), selected?.Id, search, skip, take, ct);
        return Results.Ok(new SharedEntitySelectorPageDto(page.Items.Select(ToSelector).ToList(), skip, take, page.Total, skip + page.Items.Count < page.Total));
    }

    private static async Task<IResult> GetUniverseDetailsAsync(string qid, HttpContext http, INarrativeRootRepository roots, IFictionalEntityRepository entities, IDisplayProjectionReadService display, CatalogueResourceAuthorizationService authorization, CancellationToken ct)
    {
        var root = await AuthorizedRootAsync(qid, http, roots, entities, display, authorization, ApplicationPermissionIds.MetadataRead, ct);
        return root is null ? ApiErrors.NotFound("Universe not found.") : Results.Ok(ToRootDetails(root));
    }

    private static async Task<IResult> UpdateUniverseDetailsAsync(string qid, SharedEntityDetailsUpdateRequest request, HttpContext http, INarrativeRootRepository roots, IFictionalEntityRepository entities, IEntityTimelineRepository timeline, IDisplayProjectionReadService display, CatalogueResourceAuthorizationService authorization, CancellationToken ct)
    {
        var root = await AuthorizedRootAsync(qid, http, roots, entities, display, authorization, ApplicationPermissionIds.MetadataWrite, ct);
        if (root is null) return ApiErrors.NotFound("Universe not found.");
        if (string.IsNullOrWhiteSpace(request.label)) return ApiErrors.BadRequest("A label is required.");
        await roots.UpdateUserDetailsAsync(root.Qid, request.label, request.description, ct);
        await RecordEditorEventAsync(timeline, RootHistoryId(root.Qid), "Universe", "user_field_edit", "Universe details edited in shared editor.", ct);
        return Results.Ok(ToRootDetails((await roots.FindByQidAsync(root.Qid, ct))!));
    }

    private static async Task<IResult> GetUniverseArtworkAsync(string qid, HttpContext http, INarrativeRootRepository roots, IFictionalEntityRepository entities, IEntityAssetRepository assets, IDisplayProjectionReadService display, CatalogueResourceAuthorizationService authorization, CancellationToken ct)
    {
        var root = await AuthorizedRootAsync(qid, http, roots, entities, display, authorization, ApplicationPermissionIds.ArtworkRead, ct);
        return root is null ? ApiErrors.NotFound("Universe not found.") : Results.Ok(ToArtwork(await assets.GetByEntityAsync(root.Qid, null, ct)));
    }

    private static async Task<IResult> UpdateUniverseArtworkAsync(string qid, SharedEntityArtworkUpdateRequest request, HttpContext http, INarrativeRootRepository roots, IFictionalEntityRepository entities, IEntityAssetRepository assets, IEntityTimelineRepository timeline, IDisplayProjectionReadService display, CatalogueResourceAuthorizationService authorization, CancellationToken ct)
    {
        var root = await AuthorizedRootAsync(qid, http, roots, entities, display, authorization, ApplicationPermissionIds.MetadataWrite, ct);
        if (root is null) return ApiErrors.NotFound("Universe not found.");
        var response = await UpsertUserArtworkAsync(root.Qid, SharedEntityEditorTargetKinds.Universe, request, assets, ct);
        await RecordEditorEventAsync(timeline, RootHistoryId(root.Qid), "Universe", "user_artwork_edit", "Universe artwork selected in shared editor.", ct);
        return response;
    }

    private static async Task<IResult> GetEntityContextAsync(string qid, Guid id, HttpContext http, IFictionalEntityRepository entities, IDisplayProjectionReadService display, CatalogueResourceAuthorizationService authorization, CancellationToken ct)
    {
        var entity = await AuthorizedEntityAsync(qid, id, http, entities, display, authorization, ApplicationPermissionIds.MetadataRead, ct);
        return entity is null ? ApiErrors.NotFound("Entity not found.") : Results.Ok(ToEntityContext(entity, qid));
    }

    private static async Task<IResult> GetUniverseHistoryAsync(string qid, HttpContext http, INarrativeRootRepository roots, IFictionalEntityRepository entities, IEntityTimelineRepository timeline, IDisplayProjectionReadService display, CatalogueResourceAuthorizationService authorization, CancellationToken ct)
    {
        var root = await AuthorizedRootAsync(qid, http, roots, entities, display, authorization, ApplicationPermissionIds.MetadataRead, ct);
        if (root is null) return ApiErrors.NotFound("Universe not found.");
        return Results.Ok((await timeline.GetEventsByEntityAsync(RootHistoryId(root.Qid), ct)).Select(evt => new SharedEntityHistoryEntryDto(evt.Id, evt.EventType, evt.OccurredAt, evt.Detail)));
    }

    private static async Task<IResult> GetEntityDetailsAsync(string qid, Guid id, HttpContext http, IFictionalEntityRepository entities, IDisplayProjectionReadService display, CatalogueResourceAuthorizationService authorization, CancellationToken ct)
    {
        var entity = await AuthorizedEntityAsync(qid, id, http, entities, display, authorization, ApplicationPermissionIds.MetadataRead, ct);
        return entity is null ? ApiErrors.NotFound("Entity not found.") : Results.Ok(ToEntityDetails(entity, qid));
    }

    private static async Task<IResult> UpdateEntityDetailsAsync(string qid, Guid id, SharedEntityDetailsUpdateRequest request, HttpContext http, IFictionalEntityRepository entities, IEntityTimelineRepository timeline, IDisplayProjectionReadService display, CatalogueResourceAuthorizationService authorization, CancellationToken ct)
    {
        var entity = await AuthorizedEntityAsync(qid, id, http, entities, display, authorization, ApplicationPermissionIds.MetadataWrite, ct);
        if (entity is null) return ApiErrors.NotFound("Entity not found.");
        if (string.IsNullOrWhiteSpace(request.label)) return ApiErrors.BadRequest("A label is required.");
        await entities.UpdateUserDetailsAsync(entity.Id, request.label, request.description, ct);
        await RecordEditorEventAsync(timeline, entity.Id, SharedEntityEditorTargetKinds.FictionalEntity, "user_field_edit", "Fictional entity details edited in shared editor.", ct);
        return Results.Ok(ToEntityDetails((await entities.FindByIdAsync(entity.Id, ct))!, qid));
    }

    private static async Task<IResult> GetEntityArtworkAsync(string qid, Guid id, HttpContext http, IFictionalEntityRepository entities, IEntityAssetRepository assets, IDisplayProjectionReadService display, CatalogueResourceAuthorizationService authorization, CancellationToken ct)
    {
        var entity = await AuthorizedEntityAsync(qid, id, http, entities, display, authorization, ApplicationPermissionIds.ArtworkRead, ct);
        return entity is null ? ApiErrors.NotFound("Entity not found.") : Results.Ok(ToArtwork(await assets.GetByEntityAsync(entity.Id.ToString(), null, ct)));
    }

    private static async Task<IResult> UpdateEntityArtworkAsync(string qid, Guid id, SharedEntityArtworkUpdateRequest request, HttpContext http, IFictionalEntityRepository entities, IEntityAssetRepository assets, IEntityTimelineRepository timeline, IDisplayProjectionReadService display, CatalogueResourceAuthorizationService authorization, CancellationToken ct)
    {
        var entity = await AuthorizedEntityAsync(qid, id, http, entities, display, authorization, ApplicationPermissionIds.MetadataWrite, ct);
        if (entity is null) return ApiErrors.NotFound("Entity not found.");
        var response = await UpsertUserArtworkAsync(entity.Id.ToString(), SharedEntityEditorTargetKinds.FictionalEntity, request, assets, ct);
        await RecordEditorEventAsync(timeline, entity.Id, SharedEntityEditorTargetKinds.FictionalEntity, "user_artwork_edit", "Fictional entity artwork selected in shared editor.", ct);
        return response;
    }

    private static async Task<IResult> GetAppearancesAsync(string qid, Guid id, HttpContext http, IFictionalEntityRepository entities, IDisplayProjectionReadService display, CatalogueResourceAuthorizationService authorization, CancellationToken ct)
    {
        var entity = await AuthorizedEntityAsync(qid, id, http, entities, display, authorization, ApplicationPermissionIds.MetadataRead, ct);
        if (entity is null) return ApiErrors.NotFound("Entity not found.");
        var visible = await VisibleWorksAsync(display, ct);
        return Results.Ok((await entities.GetWorkLinksAsync(entity.Id, ct)).Where(link => visible.Contains(link.WorkQid)).Select(link => new SharedEntityAppearanceDto(link.WorkQid, link.WorkLabel, link.LinkType, link.AppearanceRole, link.WorkContext, link.AnchorKind, link.AnchorValue, link.NarrativeTimeIndex, link.StartTime, link.EndTime, link.SpoilerForWorkQid, link.Provenance)));
    }

    private static async Task<IResult> GetRelationshipsAsync(string qid, Guid id, HttpContext http, IFictionalEntityRepository entities, IEntityRelationshipRepository relationships, IDisplayProjectionReadService display, CatalogueResourceAuthorizationService authorization, CancellationToken ct)
    {
        var entity = await AuthorizedEntityAsync(qid, id, http, entities, display, authorization, ApplicationPermissionIds.MetadataRead, ct);
        if (entity is null) return ApiErrors.NotFound("Entity not found.");
        var visible = await VisibleWorksAsync(display, ct);
        return Results.Ok((await relationships.GetByEntityAsync(entity.WikidataQid, ct)).Where(row => string.IsNullOrWhiteSpace(row.ContextWorkQid) || visible.Contains(row.ContextWorkQid!)).Select(row => new SharedEntityRelationshipDto(row.StatementKey, row.SubjectQid, row.RelationshipTypeValue, row.ObjectQid, row.Provenance, row.ContextWorkQid, row.Qualifiers.Select(q => new UniverseGraphQualifierDto(q.QualifierType, q.Value, q.ValueKind, q.Provenance, q.IsSupplemental, q.SourceProvider, q.Confidence)).ToList())));
    }

    private static async Task<IResult> GetTimelineAsync(string qid, Guid id, HttpContext http, IFictionalEntityRepository entities, IDisplayProjectionReadService display, CatalogueResourceAuthorizationService authorization, CancellationToken ct)
    {
        var entity = await AuthorizedEntityAsync(qid, id, http, entities, display, authorization, ApplicationPermissionIds.MetadataRead, ct);
        if (entity is null) return ApiErrors.NotFound("Entity not found.");
        var visible = await VisibleWorksAsync(display, ct);
        return Results.Ok((await entities.GetWorkLinksAsync(entity.Id, ct)).Where(link => visible.Contains(link.WorkQid) && (!string.IsNullOrWhiteSpace(link.NarrativeTimeIndex) || !string.IsNullOrWhiteSpace(link.StartTime) || !string.IsNullOrWhiteSpace(link.EndTime))).Select(link => new SharedEntityTimelineEntryDto("appearance", link.NarrativeTimeIndex ?? link.WorkLabel ?? link.WorkQid, link.StartTime, link.EndTime, link.WorkQid, link.Provenance)));
    }

    private static async Task<IResult> GetSourcesAsync(string qid, Guid id, HttpContext http, IFictionalEntityRepository entities, IDisplayProjectionReadService display, CatalogueResourceAuthorizationService authorization, CancellationToken ct)
    {
        var entity = await AuthorizedEntityAsync(qid, id, http, entities, display, authorization, ApplicationPermissionIds.MetadataRead, ct);
        if (entity is null) return ApiErrors.NotFound("Entity not found.");
        var visible = await VisibleWorksAsync(display, ct);
        return Results.Ok((await entities.GetWorkLinksAsync(entity.Id, ct)).Where(link => visible.Contains(link.WorkQid)).Select(link => new SharedEntitySourceDto("appearance", link.AppearanceKey ?? link.WorkQid, link.Provenance, link.SourceProvider, link.WorkQid)));
    }

    private static async Task<IResult> GetHistoryAsync(string qid, Guid id, HttpContext http, IFictionalEntityRepository entities, IEntityTimelineRepository timeline, IDisplayProjectionReadService display, CatalogueResourceAuthorizationService authorization, CancellationToken ct)
    {
        var entity = await AuthorizedEntityAsync(qid, id, http, entities, display, authorization, ApplicationPermissionIds.MetadataRead, ct);
        if (entity is null) return ApiErrors.NotFound("Entity not found.");
        return Results.Ok((await timeline.GetEventsByEntityAsync(entity.Id, ct)).Select(evt => new SharedEntityHistoryEntryDto(evt.Id, evt.EventType, evt.OccurredAt, evt.Detail)));
    }

    private static async Task<IResult> GetEnrichmentAsync(string qid, Guid id, HttpContext http, IFictionalEntityRepository entities, IDisplayProjectionReadService display, CatalogueResourceAuthorizationService authorization, CancellationToken ct)
    {
        var entity = await AuthorizedEntityAsync(qid, id, http, entities, display, authorization, ApplicationPermissionIds.MetadataRead, ct);
        return entity is null ? ApiErrors.NotFound("Entity not found.") : Results.Ok(new SharedEntityEnrichmentStatusDto(entity.EnrichedAt is null ? "pending" : "available", entity.EnrichedAt, entity.WikidataRevisionId));
    }

    private static async Task<IResult> RefreshEntityAsync(string qid, Guid id, HttpContext http, IFictionalEntityRepository entities, IDisplayProjectionReadService display, CatalogueResourceAuthorizationService authorization, IMetadataHarvestingService harvesting, CancellationToken ct)
    {
        var entity = await AuthorizedEntityAsync(qid, id, http, entities, display, authorization, ApplicationPermissionIds.MetadataEnrichmentRun, ct);
        if (entity is null) return ApiErrors.NotFound("Entity not found.");
        await harvesting.EnqueueAsync(new HarvestRequest { EntityId = entity.Id, EntityType = ToHarvestEntityType(entity.EntitySubType), MediaType = MediaType.Unknown, Hints = new Dictionary<string, string> { ["fictional_entity_qid"] = entity.WikidataQid, ["refresh"] = "shared_editor" } }, ct);
        return Results.Ok(new SharedEntityRefreshDto(true, "Entity enrichment refresh queued."));
    }

    private static async Task<NarrativeRoot?> AuthorizedRootAsync(string qid, HttpContext http, INarrativeRootRepository roots, IFictionalEntityRepository entities, IDisplayProjectionReadService display, CatalogueResourceAuthorizationService authorization, ApplicationPermissionId permission, CancellationToken ct)
    {
        var root = await roots.FindByQidAsync(qid, ct);
        if (root is null || await authorization.EvaluateQidAsync(http, qid, permission, ct) != CatalogueResourceAccess.Allowed) return null;
        // A root may be valid before Stage 3 has discovered its first entity. Authorize
        // through the owned work that carries its canonical narrative provenance, never
        // merely because a caller supplied a raw root QID.
        var candidateWorkIds = await roots.FindWorkIdsByProvenanceQidAsync(qid, ct);
        foreach (var workId in candidateWorkIds)
        {
            if (await authorization.EvaluateEntityAsync(http, "Work", workId, permission, ct) == CatalogueResourceAccess.Allowed)
            {
                return root;
            }
        }
        return null;
    }

    private static async Task<FictionalEntity?> AuthorizedEntityAsync(string qid, Guid id, HttpContext http, IFictionalEntityRepository entities, IDisplayProjectionReadService display, CatalogueResourceAuthorizationService authorization, ApplicationPermissionId permission, CancellationToken ct)
    {
        var entity = await entities.FindByIdAsync(id, ct);
        if (entity is null || !string.Equals(entity.FictionalUniverseQid, qid, StringComparison.OrdinalIgnoreCase) || await authorization.EvaluateEntityAsync(http, SharedEntityEditorTargetKinds.FictionalEntity, id, permission, ct) != CatalogueResourceAccess.Allowed) return null;
        return (await entities.SearchVisibleByUniverseAsync(qid, await VisibleWorksAsync(display, ct), null, entity.WikidataQid, 0, 1, ct)).Items.Any(item => item.Id == id) ? entity : null;
    }

    private static async Task<IResult> UpsertUserArtworkAsync(string entityId, string entityType, SharedEntityArtworkUpdateRequest request, IEntityAssetRepository assets, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.asset_type) || string.IsNullOrWhiteSpace(request.image_url) || !Uri.TryCreate(request.image_url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) || !Enum.TryParse<AssetType>(request.asset_type, true, out _)) return ApiErrors.BadRequest("Artwork must use a supported type and an absolute HTTP(S) URL.");
        var asset = new EntityAsset { EntityId = entityId, EntityType = entityType, AssetTypeValue = request.asset_type, ImageUrl = request.image_url, SourceProvider = "user_upload", IsUserOverride = true, IsPreferred = request.preferred, OwnerScope = entityType };
        await assets.UpsertAsync(asset, ct);
        if (request.preferred) await assets.SetPreferredAsync(asset.Id, ct);
        return Results.Ok(ToArtwork(await assets.GetByEntityAsync(entityId, null, ct)));
    }

    private static SharedEntityEditorContextDto ToEntityContext(FictionalEntity entity, string universeQid) => new(new(SharedEntityEditorTargetKinds.FictionalEntity, universeQid, entity.Id, entity.WikidataQid), entity.Label, entity.EntitySubType, Capabilities(), entity.EnrichedAt is null ? "pending" : "available");
    private static SharedEntityDetailsDto ToRootDetails(NarrativeRoot root) => new(null, root.Qid, root.Label, "Universe", root.Description, root.Qid, root.Label);
    private static SharedEntityDetailsDto ToEntityDetails(FictionalEntity entity, string universeQid) => new(entity.Id, entity.WikidataQid, entity.Label, entity.EntitySubType, entity.Description, universeQid, entity.FictionalUniverseLabel);
    private static SharedEntitySelectorItemDto ToSelector(FictionalEntity entity) => new(entity.Id, entity.WikidataQid, entity.Label, entity.EntitySubType, entity.Description);
    private static IReadOnlyList<SharedEntityArtworkDto> ToArtwork(IReadOnlyList<EntityAsset> assets) => assets.Select(asset => new SharedEntityArtworkDto(asset.Id, asset.AssetTypeValue, asset.ImageUrl, asset.IsPreferred, asset.IsUserOverride, asset.SourceProvider)).ToList();
    private static Category? ResolveCategory(string? value) => string.IsNullOrWhiteSpace(value) ? null : Categories.FirstOrDefault(category => category.Id.Equals(value, StringComparison.OrdinalIgnoreCase));
    private static IReadOnlyList<SharedEntityEditorCapabilityDto> Capabilities() => [new(SharedEntityEditorSections.Details, true, true), new(SharedEntityEditorSections.Artwork, true, true), new(SharedEntityEditorSections.Appearances, true, false, "Refreshed from enrichment."), new(SharedEntityEditorSections.Relationships, true, false, "Refreshed from enrichment."), new(SharedEntityEditorSections.Timeline, true, false, "In-universe narrative projection."), new(SharedEntityEditorSections.Sources, true, false, "Read-only provenance projection."), new(SharedEntityEditorSections.History, true, false), new(SharedEntityEditorSections.Enrichment, true, false)];
    private static async Task<HashSet<string>> VisibleWorksAsync(IDisplayProjectionReadService display, CancellationToken ct) => (await display.LoadWorksAsync(ct)).Select(work => work.IdentityQid).Where(qid => !string.IsNullOrWhiteSpace(qid)).Select(qid => qid!).ToHashSet(StringComparer.OrdinalIgnoreCase);
    private static EntityType ToHarvestEntityType(string category) => category switch
    {
        "Character" => EntityType.Character,
        "Location" => EntityType.Location,
        "Organization" => EntityType.Organization,
        "Event" => EntityType.Event,
        "Object" => EntityType.Object,
        _ => throw new ArgumentOutOfRangeException(nameof(category), category, "Unsupported fictional entity category."),
    };
    private static Guid RootHistoryId(string qid) => new(MD5.HashData(Encoding.UTF8.GetBytes("narrative-root:" + qid.Trim().ToUpperInvariant())));
    private static Task RecordEditorEventAsync(IEntityTimelineRepository timeline, Guid entityId, string entityType, string eventType, string detail, CancellationToken ct) => timeline.InsertEventAsync(new EntityEvent { EntityId = entityId, EntityType = entityType, EventType = eventType, Trigger = "api_request", Detail = detail }, ct);
}
