using System.Net;
using System.Net.Http.Json;
using MediaEngine.Web.Models.ViewDTOs;
using Microsoft.Extensions.Logging;

namespace MediaEngine.Web.Services.Integration.Clients;

public sealed class ProviderClient
{
    private readonly HttpClient _http;
    private readonly ILogger<ProviderClient> _logger;
    private readonly EngineApiFailureState _failureState;

    public ProviderClient(HttpClient http, ILogger<ProviderClient> logger, EngineApiFailureState failureState)
    {
        _http = http;
        _logger = logger;
        _failureState = failureState;
    }

    public async Task<IReadOnlyList<ProviderCatalogueDto>> GetProviderCatalogueAsync(CancellationToken ct = default)
    {
        const string endpoint = "GET /providers/catalogue";
        try
        {
            var response = await _http.GetAsync("/providers/catalogue", ct);
            if (!response.IsSuccessStatusCode)
            {
                await _failureState.RecordHttpFailureAsync(endpoint, response, _logger, ct);
                return [];
            }

            var raw = await response.Content.ReadFromJsonAsync<ProviderCatalogueDto[]>(cancellationToken: ct);
            _failureState.Clear(endpoint);
            return raw ?? [];
        }
        catch (OperationCanceledException) { return []; }
        catch (Exception ex)
        {
            _failureState.RecordExceptionFailure(endpoint, ex, _logger);
            return [];
        }
    }

    public async Task<IReadOnlyList<ProviderStatusDto>> GetProviderStatusAsync(CancellationToken ct = default)
    {
        const string endpoint = "GET /settings/providers";
        try
        {
            var response = await _http.GetAsync("/settings/providers", ct);
            if (!response.IsSuccessStatusCode)
            {
                await _failureState.RecordHttpFailureAsync(endpoint, response, _logger, ct);
                return [];
            }

            var raw = await response.Content.ReadFromJsonAsync<ProviderStatusDto[]>(cancellationToken: ct);
            _failureState.Clear(endpoint);
            return raw ?? [];
        }
        catch (OperationCanceledException) { return []; }
        catch (Exception ex)
        {
            _failureState.RecordExceptionFailure(endpoint, ex, _logger);
            return [];
        }
    }

    public async Task<bool> UpdateProviderAsync(string name, bool enabled, CancellationToken ct = default)
    {
        var endpoint = $"PUT /settings/providers/{name}";
        try
        {
            var encoded = WebUtility.UrlEncode(name);
            var resp = await _http.PutAsJsonAsync($"/settings/providers/{encoded}", new { enabled }, ct);
            if (!resp.IsSuccessStatusCode)
            {
                await _failureState.RecordHttpFailureAsync(endpoint, resp, _logger, ct);
                return false;
            }

            _failureState.Clear(endpoint);
            return true;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception ex)
        {
            _failureState.RecordExceptionFailure(endpoint, ex, _logger);
            return false;
        }
    }
}
