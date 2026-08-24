using System.Net.Http.Json;
using System.Text.Json;
using MediaEngine.Contracts.Collections;

namespace MediaEngine.Web.Services.Integration;

public sealed class CollectionPersonalMediaClient(
    HttpClient http,
    ILogger<CollectionPersonalMediaClient> logger) : ICollectionPersonalMediaClient, IDisposable
{
    public string? LastError { get; private set; }

    public async Task<IReadOnlyList<CollectionGalleryReferenceDto>> GetEligibleGalleriesAsync(CancellationToken ct = default) =>
        await GetListAsync<CollectionGalleryReferenceDto>("/collections/personal-media/galleries", ct);

    public async Task<IReadOnlyList<CollectionPersonalMediaSourceDto>> GetSourcesAsync(
        Guid collectionId,
        CancellationToken ct = default) =>
        await GetListAsync<CollectionPersonalMediaSourceDto>($"/collections/{collectionId:D}/personal-media", ct);

    public Task<CollectionPersonalMediaSourceDto?> AddSourceAsync(
        Guid collectionId,
        CollectionPersonalMediaSourceWriteRequest request,
        CancellationToken ct = default) =>
        WriteAsync(HttpMethod.Post, $"/collections/{collectionId:D}/personal-media", request, ct);

    public Task<CollectionPersonalMediaSourceDto?> UpdateSourceAsync(
        Guid collectionId,
        Guid sourceId,
        CollectionPersonalMediaSourceWriteRequest request,
        CancellationToken ct = default) =>
        WriteAsync(HttpMethod.Put, $"/collections/{collectionId:D}/personal-media/{sourceId:D}", request, ct);

    public async Task<bool> RemoveSourceAsync(Guid collectionId, Guid sourceId, CancellationToken ct = default)
    {
        try
        {
            using var response = await http.DeleteAsync($"/collections/{collectionId:D}/personal-media/{sourceId:D}", ct);
            if (response.IsSuccessStatusCode)
            {
                LastError = null;
                return true;
            }
            LastError = await ReadFailureAsync(response, ct);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(exception, "DELETE Collection personal-media source failed");
            LastError = "The Dashboard could not reach the Engine.";
        }
        return false;
    }

    private async Task<IReadOnlyList<T>> GetListAsync<T>(string url, CancellationToken ct)
    {
        try
        {
            using var response = await http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                LastError = await ReadFailureAsync(response, ct);
                return [];
            }
            LastError = null;
            return await response.Content.ReadFromJsonAsync<List<T>>(cancellationToken: ct) ?? [];
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(exception, "GET {Url} failed", url);
            LastError = "The Dashboard could not reach the Engine.";
            return [];
        }
    }

    private async Task<CollectionPersonalMediaSourceDto?> WriteAsync(
        HttpMethod method,
        string url,
        CollectionPersonalMediaSourceWriteRequest request,
        CancellationToken ct)
    {
        try
        {
            using var message = new HttpRequestMessage(method, url)
            {
                Content = JsonContent.Create(request),
            };
            using var response = await http.SendAsync(message, ct);
            if (!response.IsSuccessStatusCode)
            {
                LastError = await ReadFailureAsync(response, ct);
                return null;
            }
            LastError = null;
            return await response.Content.ReadFromJsonAsync<CollectionPersonalMediaSourceDto>(cancellationToken: ct);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(exception, "{Method} {Url} failed", method, url);
            LastError = "The Dashboard could not reach the Engine.";
            return null;
        }
    }

    private static async Task<string> ReadFailureAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            if (document.RootElement.TryGetProperty("detail", out var detail)
                && detail.ValueKind == JsonValueKind.String)
                return detail.GetString() ?? $"The Engine rejected the request ({(int)response.StatusCode}).";
            if (document.RootElement.TryGetProperty("error", out var error)
                && error.ValueKind == JsonValueKind.String)
                return error.GetString() ?? $"The Engine rejected the request ({(int)response.StatusCode}).";
        }
        catch (JsonException)
        {
            // Keep a truthful status-only fallback for non-problem responses.
        }
        return $"The Engine rejected the request ({(int)response.StatusCode}).";
    }

    public void Dispose() => http.Dispose();
}
