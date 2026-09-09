using MediaEngine.Domain.Authorization;

namespace MediaEngine.Api.Services.View;

public interface IViewResourceAuthorizationService
{
    Task<ViewAccessDecision> AuthorizeAsync(
        RequestAuthority caller,
        ViewResourceRequest request,
        CancellationToken ct = default);
}

