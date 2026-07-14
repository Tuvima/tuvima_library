using MediaEngine.Web.Models.ViewDTOs;
using Microsoft.JSInterop;

namespace MediaEngine.Web.Services.Integration;

/// <summary>
/// Stores the active local profile for the current browser.
/// </summary>
public sealed class ActiveProfileSessionService : IDisposable
{
    private const string StorageKey = "tuvima-active-profile-id";

    private readonly IJSRuntime _js;
    private readonly IEngineApiClient _api;
    private readonly SemaphoreSlim _profilesGate = new(1, 1);
    private List<ProfileViewModel> _profiles = [];
    private ProfileViewModel? _activeProfile;
    private Guid? _cachedProfileId;
    private bool _storageLoaded;
    private bool _profilesLoaded;

    public ActiveProfileSessionService(IJSRuntime js, IEngineApiClient api)
    {
        _js = js;
        _api = api;
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

    public async Task<ProfileViewModel?> SetActiveProfileAsync(Guid profileId, CancellationToken ct = default)
    {
        var profiles = await GetProfilesAsync(ct);
        var profile = profiles.FirstOrDefault(candidate => candidate.Id == profileId);
        if (profile is null)
        {
            return null;
        }

        _cachedProfileId = profileId;
        _activeProfile = profile;
        _storageLoaded = true;
        await SaveAsync(profileId, ct);
        return profile;
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

        await LoadAsync(ct);

        if (_cachedProfileId is { } activeId)
        {
            var active = profiles.FirstOrDefault(profile => profile.Id == activeId);
            if (active is not null)
            {
                return active;
            }
        }

        var fallback = profiles.FirstOrDefault(profile => profile.IsSeed)
            ?? profiles.FirstOrDefault(profile =>
                string.Equals(profile.Role, "Administrator", StringComparison.OrdinalIgnoreCase))
            ?? profiles[0];

        _cachedProfileId = fallback.Id;
        await SaveAsync(fallback.Id, ct);
        return fallback;
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        if (_storageLoaded)
        {
            return;
        }

        _storageLoaded = true;

        try
        {
            var stored = await _js.InvokeAsync<string?>("localStorage.getItem", ct, StorageKey);
            if (Guid.TryParse(stored, out var parsed))
            {
                _cachedProfileId = parsed;
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (JSException)
        {
        }
    }

    private async Task SaveAsync(Guid profileId, CancellationToken ct)
    {
        try
        {
            await _js.InvokeVoidAsync("localStorage.setItem", ct, StorageKey, profileId.ToString("D"));
        }
        catch (InvalidOperationException)
        {
        }
        catch (JSException)
        {
        }
    }

    public void Dispose() => _profilesGate.Dispose();
}
