namespace MediaEngine.Api.Services.View;

public interface IViewResourceAuthorizationService
{
    Task<ViewAccessDecision> AuthorizeAsync(
        ViewRequestProfile? caller,
        ViewResourceRequest request,
        CancellationToken ct = default);
}

