namespace MediaEngine.Api.Services.View;

public interface IViewQueryOrchestrator
{
    Task<ViewQueryResult> QueryAsync(ViewAssetQueryRequest request, CancellationToken ct = default);
}
