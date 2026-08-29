using System.Net;
using System.Net.Http.Json;
using MediaEngine.Contracts.Authentication;

namespace MediaEngine.Web.Services.Integration;

public sealed class DashboardIdentityClient(IHttpClientFactory clients)
{
    private HttpClient Client => clients.CreateClient("EngineIdentity");

    public async Task<AuthBootstrapStatusResponse?> GetBootstrapStatusAsync(CancellationToken ct = default) =>
        await Client.GetFromJsonAsync<AuthBootstrapStatusResponse>("/auth/bootstrap/status", ct).ConfigureAwait(false);

    public async Task<AuthSessionResponse?> LoginAsync(LocalLoginRequest request, CancellationToken ct = default)
    {
        using var response = await Client.PostAsJsonAsync("/auth/login", request, ct).ConfigureAwait(false);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<AuthSessionResponse>(cancellationToken: ct).ConfigureAwait(false)
            : null;
    }

    public async Task<AuthSessionResponse?> BootstrapAsync(
        BootstrapAdministratorRequest request,
        CancellationToken ct = default)
    {
        using var response = await Client.PostAsJsonAsync(
            "/auth/bootstrap/administrator",
            request,
            ct).ConfigureAwait(false);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<AuthSessionResponse>(cancellationToken: ct).ConfigureAwait(false)
            : null;
    }

    public async Task<SessionValidationResponse?> ValidateAsync(string sessionToken, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/auth/session/validate");
        request.Headers.TryAddWithoutValidation(DashboardEngineAuthenticationHandler.SessionHeader, sessionToken);
        using var response = await Client.SendAsync(request, ct).ConfigureAwait(false);
        return response.StatusCode == HttpStatusCode.Unauthorized || !response.IsSuccessStatusCode
            ? null
            : await response.Content.ReadFromJsonAsync<SessionValidationResponse>(cancellationToken: ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<string>?> RecoverAsync(RecoverPasswordRequest request, CancellationToken ct = default)
    {
        using var response = await Client.PostAsJsonAsync("/auth/password/recover", request, ct).ConfigureAwait(false);
        var result = response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<RecoveryCodesResponse>(cancellationToken: ct).ConfigureAwait(false)
            : null;
        return result?.RecoveryCodes;
    }

    public async Task<IReadOnlyList<string>?> ResetLocalAdministratorPasswordAsync(
        ResetLocalAdministratorPasswordRequest request,
        CancellationToken ct = default)
    {
        using var response = await Client.PostAsJsonAsync(
            "/auth/password/local-administrator-reset",
            request,
            ct).ConfigureAwait(false);
        var result = response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<RecoveryCodesResponse>(cancellationToken: ct).ConfigureAwait(false)
            : null;
        return result?.RecoveryCodes;
    }

    public async Task<List<DeviceSessionResponse>> GetSessionsAsync(CancellationToken ct = default) =>
        await Client.GetFromJsonAsync<List<DeviceSessionResponse>>("/auth/sessions", ct).ConfigureAwait(false) ?? [];

    public async Task<bool> RevokeSessionAsync(Guid sessionId, CancellationToken ct = default) =>
        (await Client.DeleteAsync($"/auth/sessions/{sessionId:D}", ct).ConfigureAwait(false)).IsSuccessStatusCode;

    public async Task<bool> ChangePasswordAsync(ChangePasswordRequest request, CancellationToken ct = default) =>
        (await Client.PostAsJsonAsync("/auth/password/change", request, ct).ConfigureAwait(false)).IsSuccessStatusCode;

    public async Task<SessionValidationResponse?> SwitchProfileAsync(SwitchProfileRequest request, CancellationToken ct = default)
    {
        using var response = await Client.PostAsJsonAsync("/auth/session/switch-profile", request, ct).ConfigureAwait(false);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<SessionValidationResponse>(cancellationToken: ct).ConfigureAwait(false)
            : null;
    }

    public async Task<AuthSessionResponse?> CreateExternalSessionAsync(ExternalSessionRequest request, CancellationToken ct = default)
    {
        using var response = await Client.PostAsJsonAsync("/auth/external-session", request, ct).ConfigureAwait(false);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<AuthSessionResponse>(cancellationToken: ct).ConfigureAwait(false)
            : null;
    }

    public async Task<IntercomTokenResponse?> GetIntercomTokenAsync(CancellationToken ct = default)
    {
        using var response = await Client.PostAsync("/auth/intercom-token", null, ct).ConfigureAwait(false);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<IntercomTokenResponse>(cancellationToken: ct).ConfigureAwait(false)
            : null;
    }
}
