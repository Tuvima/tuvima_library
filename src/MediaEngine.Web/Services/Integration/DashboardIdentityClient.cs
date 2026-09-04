using System.Net;
using System.Net.Http.Json;
using MediaEngine.Contracts.Authentication;
using MediaEngine.Contracts.Profiles;

namespace MediaEngine.Web.Services.Integration;

public sealed class DashboardIdentityClient(IHttpClientFactory clients,IHttpContextAccessor? contextAccessor=null)
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

    public async Task<string?> BeginPasswordResetAsync(string email,CancellationToken ct=default)
    {
        using var response=await Client.PostAsJsonAsync("/auth/password/reset/begin",new BeginPasswordResetRequest(email),ct).ConfigureAwait(false);
        return response.IsSuccessStatusCode?(await response.Content.ReadFromJsonAsync<BeginPasswordResetResponse>(cancellationToken:ct).ConfigureAwait(false))?.Token:null;
    }

    public async Task<bool> CompletePasswordResetAsync(string token,string newPassword,CancellationToken ct=default)=>
        (await Client.PostAsJsonAsync("/auth/password/reset/complete",new ResetPasswordTokenRequest(token,newPassword),ct).ConfigureAwait(false)).IsSuccessStatusCode;

    public async Task<AccountResponse?> GetAccountAsync(CancellationToken ct=default)=>
        await Client.GetFromJsonAsync<AccountResponse>("/accounts/me",ct).ConfigureAwait(false);
    public async Task<AuthSessionResponse?> AcceptInvitationAsync(AcceptAccountInvitationRequest request,CancellationToken ct=default)
    {using var response=await Client.PostAsJsonAsync("/auth/invitations/accept",request,ct).ConfigureAwait(false);return response.IsSuccessStatusCode?await response.Content.ReadFromJsonAsync<AuthSessionResponse>(cancellationToken:ct).ConfigureAwait(false):null;}

    public async Task<AccountExternalLoginDto?> LinkExternalLoginAsync(LinkAccountExternalLoginRequest request,CancellationToken ct=default)
    {
        using var response=await Client.PostAsJsonAsync("/accounts/me/external-logins",request,ct).ConfigureAwait(false);
        return response.IsSuccessStatusCode?await response.Content.ReadFromJsonAsync<AccountExternalLoginDto>(cancellationToken:ct).ConfigureAwait(false):null;
    }

    public async Task<List<AccountExternalLoginDto>> GetExternalLoginsAsync(CancellationToken ct=default)=>
        await Client.GetFromJsonAsync<List<AccountExternalLoginDto>>("/accounts/me/external-logins",ct).ConfigureAwait(false)??[];

    public async Task<bool> UnlinkExternalLoginAsync(Guid id,CancellationToken ct=default)=>
        (await Client.DeleteAsync($"/accounts/me/external-logins/{id:D}",ct).ConfigureAwait(false)).IsSuccessStatusCode;
    public async Task<List<AccountResponse>> GetAccountsAsync(CancellationToken ct=default)=>await Client.GetFromJsonAsync<List<AccountResponse>>("/accounts",ct).ConfigureAwait(false)??[];
    public async Task<AccountResponse?> CreateAccountAsync(CreateAccountRequest request,CancellationToken ct=default)
    {using var response=await Client.PostAsJsonAsync("/accounts",request,ct).ConfigureAwait(false);return response.IsSuccessStatusCode?await response.Content.ReadFromJsonAsync<AccountResponse>(cancellationToken:ct).ConfigureAwait(false):null;}
    public async Task<AccountResponse?> GrantProfileAsync(Guid accountId,Guid profileId,bool isDefault=false,CancellationToken ct=default)
    {using var response=await Client.PutAsJsonAsync($"/accounts/{accountId:D}/profiles/{profileId:D}",new SetAccountProfileGrantRequest(isDefault),ct).ConfigureAwait(false);return response.IsSuccessStatusCode?await response.Content.ReadFromJsonAsync<AccountResponse>(cancellationToken:ct).ConfigureAwait(false):null;}
    public async Task<AccountResponse?> RevokeProfileAsync(Guid accountId,Guid profileId,CancellationToken ct=default)
    {using var response=await Client.DeleteAsync($"/accounts/{accountId:D}/profiles/{profileId:D}",ct).ConfigureAwait(false);return response.IsSuccessStatusCode?await response.Content.ReadFromJsonAsync<AccountResponse>(cancellationToken:ct).ConfigureAwait(false):null;}
    public async Task<AccountInvitationResponse?> CreateInvitationAsync(CreateAccountInvitationRequest request,CancellationToken ct=default)
    {using var response=await Client.PostAsJsonAsync("/accounts/invitations",request,ct).ConfigureAwait(false);return response.IsSuccessStatusCode?await response.Content.ReadFromJsonAsync<AccountInvitationResponse>(cancellationToken:ct).ConfigureAwait(false):null;}

    public Task<PasskeyOptionsResponse?> GetPasskeyLoginOptionsAsync(string? email,CancellationToken ct=default)=>
        SendPasskeyAsync<BeginPasskeyLoginRequest,PasskeyOptionsResponse>("/auth/passkeys/login/options",new(email),ct);
    public Task<AuthSessionResponse?> CompletePasskeyLoginAsync(CompletePasskeyLoginRequest body,CancellationToken ct=default)=>
        SendPasskeyAsync<CompletePasskeyLoginRequest,AuthSessionResponse>("/auth/passkeys/login/complete",body,ct);
    public Task<PasskeyOptionsResponse?> GetPasskeyRegistrationOptionsAsync(CancellationToken ct=default)=>
        SendPasskeyAsync<object,PasskeyOptionsResponse>("/auth/passkeys/registration/options",new{},ct);
    public async Task<bool> CompletePasskeyRegistrationAsync(CompletePasskeyRegistrationRequest body,CancellationToken ct=default)
    {using var request=PasskeyRequest(HttpMethod.Post,"/auth/passkeys/registration/complete",body);using var response=await Client.SendAsync(request,ct).ConfigureAwait(false);return response.IsSuccessStatusCode;}
    public async Task<List<PasskeyCredentialResponse>> GetPasskeysAsync(CancellationToken ct=default)=>await Client.GetFromJsonAsync<List<PasskeyCredentialResponse>>("/auth/passkeys",ct).ConfigureAwait(false)??[];
    public async Task<bool> RemovePasskeyAsync(string id,CancellationToken ct=default)=>(await Client.DeleteAsync($"/auth/passkeys/{Uri.EscapeDataString(id)}",ct).ConfigureAwait(false)).IsSuccessStatusCode;
    public async Task<AdministratorElevationResponse?> GetElevationAsync(CancellationToken ct=default)=>await Client.GetFromJsonAsync<AdministratorElevationResponse>("/auth/elevation",ct).ConfigureAwait(false);
    public async Task<AdministratorElevationResponse?> ElevateAsync(string secret,CancellationToken ct=default)
    {using var response=await Client.PostAsJsonAsync("/auth/elevation",new ElevateAdministratorRequest{Secret=secret},ct).ConfigureAwait(false);return response.IsSuccessStatusCode?await response.Content.ReadFromJsonAsync<AdministratorElevationResponse>(cancellationToken:ct).ConfigureAwait(false):null;}
    public async Task<bool> SetAdministratorPinAsync(Guid profileId,string? pin,CancellationToken ct=default)=>
        (await Client.PutAsJsonAsync($"/auth/profiles/{profileId:D}/administrator-pin",new SetProfilePinRequest{Pin=pin??string.Empty},ct).ConfigureAwait(false)).IsSuccessStatusCode;
    public Task<PasskeyOptionsResponse?> GetPasskeyElevationOptionsAsync(CancellationToken ct=default)=>SendPasskeyAsync<object,PasskeyOptionsResponse>("/auth/passkeys/elevation/options",new{},ct);
    public Task<AdministratorElevationResponse?> CompletePasskeyElevationAsync(CompletePasskeyElevationRequest body,CancellationToken ct=default)=>SendPasskeyAsync<CompletePasskeyElevationRequest,AdministratorElevationResponse>("/auth/passkeys/elevation/complete",body,ct);

    private async Task<TResponse?> SendPasskeyAsync<TRequest,TResponse>(string path,TRequest body,CancellationToken ct)
    {using var request=PasskeyRequest(HttpMethod.Post,path,body);using var response=await Client.SendAsync(request,ct).ConfigureAwait(false);return response.IsSuccessStatusCode?await response.Content.ReadFromJsonAsync<TResponse>(cancellationToken:ct).ConfigureAwait(false):default;}
    private HttpRequestMessage PasskeyRequest<T>(HttpMethod method,string path,T body)
    {
        var request=new HttpRequestMessage(method,path){Content=JsonContent.Create(body)};var inbound=contextAccessor?.HttpContext?.Request;
        if(inbound is not null){request.Headers.Host=inbound.Host.Value;request.Headers.TryAddWithoutValidation("Origin",$"{inbound.Scheme}://{inbound.Host.Value}");}
        return request;
    }

    public async Task<List<DeviceSessionResponse>> GetSessionsAsync(CancellationToken ct = default) =>
        await Client.GetFromJsonAsync<List<DeviceSessionResponse>>("/auth/sessions", ct).ConfigureAwait(false) ?? [];

    public async Task<bool> RevokeSessionAsync(Guid sessionId, CancellationToken ct = default) =>
        (await Client.DeleteAsync($"/auth/sessions/{sessionId:D}", ct).ConfigureAwait(false)).IsSuccessStatusCode;

    public async Task<bool> ChangePasswordAsync(ChangePasswordRequest request, CancellationToken ct = default) =>
        (await Client.PostAsJsonAsync("/auth/password/change", request, ct).ConfigureAwait(false)).IsSuccessStatusCode;

    public async Task<IReadOnlyList<string>?> RegenerateRecoveryCodesAsync(string currentPassword, CancellationToken ct = default)
    {
        using var response = await Client.PostAsJsonAsync("/auth/password/recovery-codes", new RegenerateRecoveryCodesRequest(currentPassword), ct).ConfigureAwait(false);
        return response.IsSuccessStatusCode
            ? (await response.Content.ReadFromJsonAsync<RecoveryCodesResponse>(cancellationToken: ct).ConfigureAwait(false))?.RecoveryCodes
            : null;
    }

    public async Task<DashboardProfileSwitchResult> SwitchProfileAsync(SwitchProfileRequest request, CancellationToken ct = default)
    {
        using var response = await Client.PostAsJsonAsync("/auth/session/switch-profile", request, ct).ConfigureAwait(false);
        if (response.IsSuccessStatusCode)
        {
            var session = await response.Content.ReadFromJsonAsync<SessionValidationResponse>(cancellationToken: ct).ConfigureAwait(false);
            return session is null
                ? new DashboardProfileSwitchResult(DashboardProfileSwitchStatus.Failed)
                : new DashboardProfileSwitchResult(DashboardProfileSwitchStatus.Succeeded, session);
        }

        return response.StatusCode switch
        {
            HttpStatusCode.PreconditionRequired => new DashboardProfileSwitchResult(DashboardProfileSwitchStatus.PinRequired),
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => new DashboardProfileSwitchResult(DashboardProfileSwitchStatus.Forbidden),
            HttpStatusCode.NotFound => new DashboardProfileSwitchResult(DashboardProfileSwitchStatus.NotFound),
            _ => new DashboardProfileSwitchResult(DashboardProfileSwitchStatus.Failed),
        };
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

public enum DashboardProfileSwitchStatus
{
    Succeeded,
    PinRequired,
    Forbidden,
    NotFound,
    Failed,
}

public sealed record DashboardProfileSwitchResult(
    DashboardProfileSwitchStatus Status,
    SessionValidationResponse? Session = null);
