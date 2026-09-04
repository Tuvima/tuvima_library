using System.Net.Http.Json;
using MediaEngine.Contracts.Setup;

namespace MediaEngine.Web.Services.Integration;

public sealed partial class EngineApiClient
{
    public Task<SetupStatusDto?> GetSetupStatusAsync(CancellationToken ct = default) =>
        SetupSendAsync<SetupStatusDto>(HttpMethod.Get, "/setup/v1/status", null, null, ct);

    public Task<SetupStartResponse?> BeginSetupAsync(CancellationToken ct = default) =>
        SetupSendAsync<SetupStartResponse>(HttpMethod.Post, "/setup/v1/begin", JsonContent.Create(new { }), null, ct);

    public Task<SetupPreflightDto?> RunSetupPreflightAsync(string? setupSession, CancellationToken ct = default) =>
        SetupSendAsync<SetupPreflightDto>(HttpMethod.Post, "/setup/v1/preflight", JsonContent.Create(new { }), setupSession, ct);

    public Task<SetupAdministratorResponse?> CreateSetupAdministratorAsync(SetupAdministratorRequest request, string setupSession, CancellationToken ct = default) =>
        SetupSendAsync<SetupAdministratorResponse>(HttpMethod.Post, "/setup/v1/administrator", JsonContent.Create(request), setupSession, ct);

    public Task<SetupMediaLocationsDto?> ValidateSetupMediaLocationsAsync(string? setupSession, CancellationToken ct = default) =>
        SetupSendAsync<SetupMediaLocationsDto>(HttpMethod.Post, "/setup/v1/media-locations/validate", JsonContent.Create(new { }), setupSession, ct);

    public Task<SetupStatusDto?> DecideSetupStepAsync(string stepKey, string status, string? detail, string? setupSession, CancellationToken ct = default) =>
        SetupSendAsync<SetupStatusDto>(HttpMethod.Post, $"/setup/v1/steps/{Uri.EscapeDataString(stepKey)}",
            JsonContent.Create(new SetupStepDecisionRequest { Status = status, Detail = detail }), setupSession, ct);

    public async Task<SetupBackupInspectionDto?> UploadSetupBackupAsync(Stream stream, string fileName, string setupSession, CancellationToken ct = default)
    {
        using var content = new MultipartFormDataContent();
        var file = new StreamContent(stream);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/zip");
        content.Add(file, "backup", fileName);
        return await SetupSendAsync<SetupBackupInspectionDto>(HttpMethod.Post, "/setup/v1/restore/upload", content, setupSession, ct);
    }

    public Task<SetupRestoreConfirmationDto?> ConfirmSetupRestoreAsync(Guid operationId, string setupSession, CancellationToken ct = default) =>
        SetupSendAsync<SetupRestoreConfirmationDto>(HttpMethod.Post, $"/setup/v1/restore/{operationId:D}/confirm", JsonContent.Create(new { }), setupSession, ct);

    public Task<SetupReadinessDto?> GetSetupReadinessAsync(string? setupSession, CancellationToken ct = default) =>
        SetupSendAsync<SetupReadinessDto>(HttpMethod.Get, "/setup/v1/readiness", null, setupSession, ct);

    public Task<SetupStatusDto?> CompleteSetupAsync(string? setupSession, CancellationToken ct = default) =>
        SetupSendAsync<SetupStatusDto>(HttpMethod.Post, "/setup/v1/complete", JsonContent.Create(new { }), setupSession, ct);

    private async Task<T?> SetupSendAsync<T>(HttpMethod method, string path, HttpContent? content, string? setupSession, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(method, path) { Content = content };
            if (!string.IsNullOrWhiteSpace(setupSession))
                request.Headers.TryAddWithoutValidation("X-Tuvima-Setup-Session", setupSession);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Setup request {Method} {Path} failed with {Status}", method, path, response.StatusCode);
                return default;
            }
            return await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { return default; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Setup request {Method} {Path} failed", method, path);
            return default;
        }
    }
}
