using MediaEngine.Web.Models.ViewDTOs;
using Microsoft.JSInterop;
using Microsoft.AspNetCore.Components.Authorization;
using System.Security.Claims;

namespace MediaEngine.Web.Services.Integration;

/// <summary>
/// Stores the active local profile for the current browser.
/// </summary>
public sealed class ActiveProfileSessionService : IDisposable
{
    private readonly IEngineApiClient _api;
    private readonly ActiveProfileAccessor _activeProfileAccessor;
    private readonly AuthenticationStateProvider? _authenticationStateProvider;
    private readonly DashboardSessionAccessor? _dashboardSession;
    private readonly DashboardIdentityClient? _identityClient;
    private readonly SemaphoreSlim _profilesGate = new(1, 1);
    private List<ProfileViewModel> _profiles = [];
    private ProfileViewModel? _activeProfile;
    private bool _profilesLoaded;

    public ActiveProfileSessionService(
        IJSRuntime js,
        IEngineApiClient api,
        ActiveProfileAccessor? activeProfileAccessor = null,
        AuthenticationStateProvider? authenticationStateProvider = null,
        DashboardSessionAccessor? dashboardSession = null,
        DashboardIdentityClient? identityClient = null)
    {
        _ = js;
        _api = api;
        _activeProfileAccessor = activeProfileAccessor ?? new ActiveProfileAccessor();
        _authenticationStateProvider = authenticationStateProvider;
        _dashboardSession = dashboardSession;
        _identityClient = identityClient;
    }

    public ProfileViewModel? CurrentProfile => _activeProfile;

    public IReadOnlyList<ProfileViewModel> Profiles => _profiles;

    public async Task<List<ProfileViewModel>> GetProfilesAsync(
        CancellationToken ct = default,
        bool forceRefresh = false)
    {
        if (_profilesLoaded && !forceRefresh)
        {
            return [.. _profiles];
        }

        await _profilesGate.WaitAsync(ct);
        try
        {
            if (_profilesLoaded && !forceRefresh)
            {
                return [.. _profiles];
            }

            _profiles = await _api.GetProfilesAsync(ct);
            _profilesLoaded = true;
            _activeProfile = await ResolveActiveProfileAsync(_profiles, ct);
            _activeProfileAccessor.SetProfile(_activeProfile?.Id);
            return [.. _profiles];
        }
        finally
        {
            _profilesGate.Release();
        }
    }

    public async Task<ProfileViewModel?> GetActiveProfileAsync(CancellationToken ct = default)
    {
        if (_profilesLoaded)
        {
            return _activeProfile;
        }

        await GetProfilesAsync(ct);
        return _activeProfile;
    }

    public async Task<ProfileSwitchOutcome> SetActiveProfileAsync(
        Guid profileId,
        string? secret = null,
        CancellationToken ct = default)
    {
        var profiles = await GetProfilesAsync(ct);
        var profile = profiles.FirstOrDefault(candidate => candidate.Id == profileId);
        if (profile is null)
        {
            return new ProfileSwitchOutcome(ProfileSwitchStatus.NotFound);
        }

        if (_identityClient is not null && _dashboardSession?.SessionToken is not null)
        {
            var switched = await _identityClient.SwitchProfileAsync(
                new MediaEngine.Contracts.Authentication.SwitchProfileRequest { ProfileId = profileId, Secret = secret }, ct);
            if (switched.Status != DashboardProfileSwitchStatus.Succeeded || switched.Session is null)
            {
                return new ProfileSwitchOutcome(switched.Status switch
                {
                    DashboardProfileSwitchStatus.PinRequired => ProfileSwitchStatus.PinRequired,
                    DashboardProfileSwitchStatus.Forbidden => ProfileSwitchStatus.Forbidden,
                    DashboardProfileSwitchStatus.NotFound => ProfileSwitchStatus.NotFound,
                    _ => ProfileSwitchStatus.Failed,
                });
            }

            var session = switched.Session;
            _dashboardSession.Set(
                _dashboardSession.SessionToken,
                session.AccountId,
                session.ActiveProfileId,
                session.SessionId,
                session.Role);
        }

        _activeProfile = profile;
        _activeProfileAccessor.SetProfile(profileId);
        return new ProfileSwitchOutcome(ProfileSwitchStatus.Succeeded, profile);
    }

    public Task<List<ProfileViewModel>> RefreshProfilesAsync(CancellationToken ct = default) =>
        GetProfilesAsync(ct, forceRefresh: true);

    public void UpsertProfile(ProfileViewModel profile)
    {
        if (_profilesLoaded)
        {
            var index = _profiles.FindIndex(candidate => candidate.Id == profile.Id);
            if (index >= 0)
            {
                _profiles[index] = profile;
            }
            else
            {
                _profiles.Add(profile);
            }
        }

        if (_activeProfile?.Id == profile.Id)
        {
            _activeProfile = profile;
        }
    }

    private async Task<ProfileViewModel?> ResolveActiveProfileAsync(
        IReadOnlyList<ProfileViewModel> profiles,
        CancellationToken ct)
    {
        if (profiles.Count == 0)
        {
            return null;
        }

        if (_authenticationStateProvider is not null)
        {
            var principal = (await _authenticationStateProvider.GetAuthenticationStateAsync()).User;
            var token = principal.FindFirstValue(DashboardEngineAuthenticationHandler.SessionTokenClaim);
            var accountId = ParseGuid(principal.FindFirstValue("tuvima:account_id"));
            var activeId = ParseGuid(principal.FindFirstValue("tuvima:active_profile_id"));
            var sessionId = ParseGuid(principal.FindFirstValue("tuvima:session_id"));
            var role = principal.FindFirstValue(ClaimTypes.Role);
            _dashboardSession?.Set(token, accountId, activeId, sessionId, role);

            if (activeId is { } authenticatedActiveId)
            {
                return profiles.FirstOrDefault(profile => profile.Id == authenticatedActiveId);
            }
        }

        // The production Dashboard always registers AuthenticationStateProvider. This
        // fallback exists only for isolated component/service tests without an auth host.
        return _authenticationStateProvider is null ? profiles.FirstOrDefault() : null;
    }

    private static Guid? ParseGuid(string? value) => Guid.TryParse(value, out var parsed) ? parsed : null;

    public void Dispose() => _profilesGate.Dispose();
}
