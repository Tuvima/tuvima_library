using MediaEngine.Domain.PersonalMedia;

namespace MediaEngine.Api.Services.View;

public enum ViewResourceKind
{
    Asset,
    Thumbnail,
    Original,
    Search,
    Gallery,
}

public enum ViewResourceAction
{
    Read,
    Contribute,
    Manage,
}

public enum ViewAccessOutcome
{
    Allowed,
    Unauthenticated,
    NotFound,
}

public sealed record ViewResourceDescriptor(
    ViewResourceKind Kind,
    Guid ResourceId,
    Guid OwnerProfileId,
    Guid? LibraryId,
    IReadOnlySet<Guid>? SharedWithProfileIds = null,
    IReadOnlySet<Guid>? ContributingProfileIds = null);

public sealed record ViewResourceRequest(
    ViewScopeRequest Scope,
    ViewResourceKind Kind,
    Guid? ResourceId,
    ViewResourceAction Action = ViewResourceAction.Read);

public sealed record ViewAccessDecision(
    ViewAccessOutcome Outcome,
    ResolvedViewScope? Scope = null)
{
    public bool IsAllowed => Outcome == ViewAccessOutcome.Allowed;

    public static ViewAccessDecision Allowed(ResolvedViewScope scope) =>
        new(ViewAccessOutcome.Allowed, scope);

    public static ViewAccessDecision Unauthenticated() =>
        new(ViewAccessOutcome.Unauthenticated);

    public static ViewAccessDecision NotFound(ResolvedViewScope? scope = null) =>
        new(ViewAccessOutcome.NotFound, scope);
}

/// <summary>
/// Single policy boundary for assets, derivatives, originals, search, and
/// Galleries. Denials after authentication intentionally collapse to NotFound
/// so profile, library, and resource existence cannot be enumerated.
/// </summary>
public sealed class ViewResourceAuthorizationService(
    IViewScopeResolver scopeResolver,
    IViewResourceStore resourceStore) : IViewResourceAuthorizationService
{
    public async Task<ViewAccessDecision> AuthorizeAsync(
        ViewRequestProfile? caller,
        ViewResourceRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (caller is null)
        {
            return ViewAccessDecision.Unauthenticated();
        }

        var resolution = await scopeResolver.ResolveAsync(caller, request.Scope, ct).ConfigureAwait(false);
        if (resolution is null)
        {
            return ViewAccessDecision.NotFound();
        }

        if (request.Kind == ViewResourceKind.Search)
        {
            return request.Action == ViewResourceAction.Read
                ? ViewAccessDecision.Allowed(resolution.Scope)
                : ViewAccessDecision.NotFound(resolution.Scope);
        }

        if (request.ResourceId is not { } resourceId)
        {
            return ViewAccessDecision.NotFound(resolution.Scope);
        }

        var resource = await resourceStore.FindAsync(
            request.Kind,
            resourceId,
            caller.ProfileId,
            ct).ConfigureAwait(false);
        if (resource is null || resource.Kind != request.Kind)
        {
            return ViewAccessDecision.NotFound(resolution.Scope);
        }

        // Explicit shares are independent of Shared View access and inclusion.
        // For an asset/derivative, the store may populate SharedWithProfileIds
        // only after proving that asset belongs to a Gallery shared with the
        // caller. This grants the specific resource, never the owner's Space.
        var explicitlyShared = resource.SharedWithProfileIds?.Contains(caller.ProfileId) == true;
        var mayContribute = resource.ContributingProfileIds?.Contains(caller.ProfileId) == true;
        if ((request.Action == ViewResourceAction.Read
                && (explicitlyShared
                || (request.Kind == ViewResourceKind.Gallery
                    && resource.OwnerProfileId == caller.ProfileId)))
            || (request.Kind == ViewResourceKind.Gallery
                && request.Action == ViewResourceAction.Contribute
                && mayContribute))
        {
            var exactScope = resource.LibraryId is { } grantedLibrary
                && !resolution.Scope.ContainsLibrary(grantedLibrary)
                    ? new ResolvedViewScope(
                        ViewScopeKind.Profile,
                        resource.OwnerProfileId,
                        new HashSet<Guid> { grantedLibrary })
                    : resolution.Scope;
            return ViewAccessDecision.Allowed(exactScope);
        }

        if (resource.LibraryId is not { } libraryId
            || !resolution.Scope.ContainsLibrary(libraryId))
        {
            return ViewAccessDecision.NotFound(resolution.Scope);
        }

        if (request.Action is ViewResourceAction.Contribute or ViewResourceAction.Manage)
        {
            var ownsResource = resource.OwnerProfileId == caller.ProfileId;
            var isMine = resolution.Scope.Kind == ViewScopeKind.Mine;
            return ownsResource && isMine
                ? ViewAccessDecision.Allowed(resolution.Scope)
                : ViewAccessDecision.NotFound(resolution.Scope);
        }

        return request.Kind != ViewResourceKind.Gallery
            ? ViewAccessDecision.Allowed(resolution.Scope)
            : ViewAccessDecision.NotFound(resolution.Scope);
    }
}
