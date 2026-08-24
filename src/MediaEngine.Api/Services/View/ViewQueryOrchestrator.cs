using MediaEngine.Contracts.LocalAssets;
using MediaEngine.Domain.Models;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Api.Services.View;

public sealed record ViewAssetQueryRequest(
    ViewScopeRequest Scope,
    int Limit = 120,
    string? Cursor = null,
    string? Search = null,
    IReadOnlyList<string>? MediaKinds = null,
    bool FavoritesOnly = false,
    bool IncludeHidden = false,
    Guid? GalleryId = null,
    LocalAssetLifecycleFilter Lifecycle = LocalAssetLifecycleFilter.Active);

/// <summary>
/// Authorized persistence plan. Backends receive only library IDs approved by
/// the resolver and must apply the whole set in the same query.
/// </summary>
public sealed record ViewAssetQueryPlan(
    ResolvedViewScope Scope,
    int Limit,
    string? Cursor,
    string? Search,
    IReadOnlyList<string>? MediaKinds,
    bool FavoritesOnly,
    bool IncludeHidden,
    Guid? GalleryId,
    LocalAssetLifecycleFilter Lifecycle,
    CollectionRuleDefinition? SmartRule);

public sealed record ViewQueryResult(
    ViewAccessOutcome Outcome,
    ViewAssetTimelinePageDto? Page = null,
    ResolvedViewScope? Scope = null);

/// <summary>
/// Authorized query boundary. Endpoint code cannot supply a profile or physical
/// library ID; both come from trusted context and scope resolution.
/// </summary>
public sealed class ViewQueryOrchestrator(
    IViewRequestProfileContext profileContext,
    IViewResourceAuthorizationService authorization,
    IViewAssetQueryBackend backend,
    IViewSmartGalleryQueryService? smartGalleries = null) : IViewQueryOrchestrator
{
    public async Task<ViewQueryResult> QueryAsync(
        ViewAssetQueryRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Limit is < 1 or > 500)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "View query limit must be between 1 and 500.");
        }

        var decision = await authorization.AuthorizeAsync(
            profileContext.Current,
            new ViewResourceRequest(
                request.Scope,
                request.GalleryId.HasValue ? ViewResourceKind.Gallery : ViewResourceKind.Search,
                request.GalleryId),
            ct).ConfigureAwait(false);
        if (!decision.IsAllowed || decision.Scope is null)
        {
            return new ViewQueryResult(decision.Outcome);
        }

        CollectionRuleDefinition? smartRule = null;
        if (request.GalleryId is { } galleryId)
        {
            if (smartGalleries is null)
                throw new InvalidOperationException("Smart Gallery query services are unavailable.");
            smartRule = await smartGalleries.ResolveRuleAsync(galleryId, ct).ConfigureAwait(false);
        }

        var plan = new ViewAssetQueryPlan(
            decision.Scope,
            request.Limit,
            request.Cursor,
            request.Search,
            request.MediaKinds,
            request.FavoritesOnly,
            request.IncludeHidden,
            smartRule is null ? request.GalleryId : null,
            request.Lifecycle,
            smartRule);
        var page = await backend.QueryAsync(plan, ct).ConfigureAwait(false);
        return new ViewQueryResult(ViewAccessOutcome.Allowed, page, decision.Scope);
    }
}
