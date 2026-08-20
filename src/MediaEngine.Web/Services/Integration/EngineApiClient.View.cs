using MediaEngine.Contracts.LocalAssets;
using MediaEngine.Contracts.Ingestion;
using System.Net.Http.Headers;
using System.Text.Json;

namespace MediaEngine.Web.Services.Integration;

public sealed partial class EngineApiClient
{
    public Task<IReadOnlyList<ViewLibrarySummaryDto>> GetViewLibrariesAsync(Guid profileId, CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<ViewLibrarySummaryDto>>(
            "GET /view/libraries",
            "/view/libraries",
            static () => [],
            new Dictionary<string, string?> { ["profileId"] = profileId.ToString("D") },
            ct: ct);

    public Task<LocalAssetPageDto?> GetViewItemsAsync(
        Guid libraryId,
        Guid profileId,
        string? search = null,
        string? kind = null,
        bool favorites = false,
        bool hidden = false,
        int offset = 0,
        int limit = 120,
        CancellationToken ct = default)
    {
        var query = new Dictionary<string, string?>
        {
            ["profileId"] = profileId.ToString("D"),
            ["q"] = string.IsNullOrWhiteSpace(search) ? null : search.Trim(),
            ["kind"] = string.IsNullOrWhiteSpace(kind) ? null : kind.Trim(),
            ["favorite"] = favorites ? "true" : null,
            ["hidden"] = hidden ? "true" : null,
            ["offset"] = Math.Max(0, offset).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["limit"] = Math.Clamp(limit, 1, 500).ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
        return GetAsync<LocalAssetPageDto>(
            $"GET /view/{libraryId:D}",
            $"/view/{libraryId:D}",
            query,
            ct: ct);
    }

    public Task<LocalAssetScanResultDto?> ScanViewLibraryAsync(Guid libraryId, Guid profileId, CancellationToken ct = default) =>
        PostAsync<object, LocalAssetScanResultDto>(
            $"POST /view/{libraryId:D}/scan",
            $"/view/{libraryId:D}/scan?profileId={profileId:D}",
            new { },
            ct: ct);

    public Task<bool> SetViewItemFavoriteAsync(
        Guid libraryId,
        Guid itemId,
        Guid profileId,
        bool value,
        CancellationToken ct = default) =>
        SetViewItemFlagAsync(libraryId, itemId, profileId, "favorite", value, ct);

    public Task<bool> SetViewItemHiddenAsync(
        Guid libraryId,
        Guid itemId,
        Guid profileId,
        bool value,
        CancellationToken ct = default) =>
        SetViewItemFlagAsync(libraryId, itemId, profileId, "hidden", value, ct);

    public async Task<ViewMediaUploadResult> UploadViewMediaAsync(
        Guid destinationLibraryId,
        Stream fileStream,
        string fileName,
        string? contentType = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(fileStream);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        try
        {
            using var content = new MultipartFormDataContent();
            using var fileContent = new StreamContent(fileStream);
            if (MediaTypeHeaderValue.TryParse(contentType, out var mediaType))
                fileContent.Headers.ContentType = mediaType;
            content.Add(fileContent, "file", Path.GetFileName(fileName));
            content.Add(new StringContent(destinationLibraryId.ToString("D")), "destinationLibraryId");

            using var response = await _http.PostAsync("/ingestion/upload", content, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return new ViewMediaUploadResult(
                    false,
                    ErrorMessage: await ReadUploadErrorAsync(response, ct).ConfigureAwait(false));
            }

            var upload = await response.Content
                .ReadFromJsonAsync<UploadMediaResponse>(cancellationToken: ct)
                .ConfigureAwait(false);
            return upload is null
                ? new ViewMediaUploadResult(false, ErrorMessage: "The Engine accepted the upload but returned no result.")
                : new ViewMediaUploadResult(true, upload);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new ViewMediaUploadResult(false, ErrorMessage: "The upload timed out before it completed.");
        }
        catch (OperationCanceledException)
        {
            return new ViewMediaUploadResult(false, ErrorMessage: "The upload was canceled.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "POST /ingestion/upload failed for View library {LibraryId}", destinationLibraryId);
            return new ViewMediaUploadResult(false, ErrorMessage: "The Dashboard could not reach the Engine to upload this file.");
        }
    }

    private Task<bool> SetViewItemFlagAsync(
        Guid libraryId,
        Guid itemId,
        Guid profileId,
        string flag,
        bool value,
        CancellationToken ct) =>
        PutAsync(
            $"PUT /view/{libraryId:D}/items/{itemId:D}/{flag}",
            $"/view/{libraryId:D}/items/{itemId:D}/{flag}?profileId={profileId:D}",
            new SetLocalAssetFlagRequest(value),
            ct: ct);

    private static async Task<string> ReadUploadErrorAsync(
        HttpResponseMessage response,
        CancellationToken ct)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
            if (document.RootElement.TryGetProperty("detail", out var detail)
                && !string.IsNullOrWhiteSpace(detail.GetString()))
            {
                return detail.GetString()!;
            }
            if (document.RootElement.TryGetProperty("title", out var title)
                && !string.IsNullOrWhiteSpace(title.GetString()))
            {
                return title.GetString()!;
            }
        }
        catch (JsonException)
        {
            // Fall back to a truthful status-based message for non-problem responses.
        }

        return $"The Engine rejected the upload ({(int)response.StatusCode} {response.ReasonPhrase}).";
    }
}
