namespace MediaEngine.Api.Services.View;

/// <summary>
/// Narrow persistence seam for View scope policy. Implementations must return
/// only current state; callers never receive physical libraries until after the
/// resolver applies policy.
/// </summary>
public interface IViewScopeStore
{
    Task<ViewProfileScopeState?> FindProfileAsync(Guid profileId, CancellationToken ct = default);

    Task<IReadOnlyList<ViewProfileScopeState>> GetProfilesAsync(CancellationToken ct = default);
}

