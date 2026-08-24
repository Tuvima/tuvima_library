using MediaEngine.Contracts.LocalAssets;

namespace MediaEngine.Api.Services.View;

public interface IViewAssetQueryBackend
{
    Task<ViewAssetTimelinePageDto> QueryAsync(ViewAssetQueryPlan plan, CancellationToken ct = default);
}
