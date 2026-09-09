using MediaEngine.Domain.Authorization;

namespace MediaEngine.Api.Services.View;

public interface IViewScopeResolver
{
    Task<ViewScopeResolution?> ResolveAsync(
        RequestAuthority caller,
        ViewScopeRequest requested,
        bool allowStaleSelectionFallback = false,
        CancellationToken ct = default);
}

