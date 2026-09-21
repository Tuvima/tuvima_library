using System.Net.Http.Json;
using MediaEngine.Contracts.Artwork;

namespace MediaEngine.Web.Services.Integration;

public sealed partial class EngineApiClient
{
    public async Task<ArtworkLibraryPageDto?> GetArtworkLibraryAsync(
        string? entityKind = null,
        string? artworkType = null,
        string? search = null,
        int offset = 0,
        int limit = 48,
        CancellationToken ct = default)
    {
        const string endpoint = "GET /api/v1/display/artwork";
        try
        {
            var query = new List<string>();
            AddQuery(query, "entityKind", entityKind);
            AddQuery(query, "artworkType", artworkType);
            AddQuery(query, "search", search);
            AddQuery(query, "offset", offset.ToString(System.Globalization.CultureInfo.InvariantCulture));
            AddQuery(query, "limit", limit.ToString(System.Globalization.CultureInfo.InvariantCulture));
            var url = "/api/v1/display/artwork?" + string.Join("&", query);
            var response = await _http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                await RecordHttpFailureAsync(endpoint, response, ct);
                return null;
            }

            var page = await response.Content.ReadFromJsonAsync<ArtworkLibraryPageDto>(cancellationToken: ct);
            if (page is null)
            {
                return null;
            }

            ClearFailure(endpoint);
            return page with
            {
                Items = page.Items.Select(item => item with
                {
                    ImageUrl = string.IsNullOrWhiteSpace(item.ImageUrl) ? null : AbsoluteUrl(item.ImageUrl),
                }).ToList(),
            };
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /api/v1/display/artwork failed");
            RecordExceptionFailure(endpoint, ex);
            return null;
        }
    }
}
