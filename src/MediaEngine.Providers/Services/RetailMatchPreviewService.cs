using MediaEngine.Domain.Models;

namespace MediaEngine.Providers.Services;

/// <summary>
/// Read-only editor boundary over the automatic retail candidate path.
/// </summary>
public sealed class RetailMatchPreviewService(SearchService searchService)
{
    public Task<SearchRetailResult> SearchAsync(
        SearchRetailRequest request,
        CancellationToken ct = default) =>
        searchService.SearchRetailAutomaticAsync(request, ct);
}
