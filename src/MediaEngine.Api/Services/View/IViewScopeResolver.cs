namespace MediaEngine.Api.Services.View;

public interface IViewScopeResolver
{
    Task<ViewScopeResolution?> ResolveAsync(
        ViewRequestProfile caller,
        ViewScopeRequest requested,
        CancellationToken ct = default);
}

