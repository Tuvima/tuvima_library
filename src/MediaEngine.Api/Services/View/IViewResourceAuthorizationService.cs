using MediaEngine.Domain.Authorization;

namespace MediaEngine.Api.Services.View;

public interface IViewResourceAuthorizationService
{
    Task<ViewAccessOutcome> AuthorizePersonalScopeBootstrapAsync(
        RequestAuthority caller,
        CancellationToken ct = default);

    Task<ViewAccessDecision> AuthorizeAsync(
        RequestAuthority caller,
        ViewResourceRequest request,
        CancellationToken ct = default);
}

