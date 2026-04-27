using MediaEngine.Web.Models.ViewDTOs;
using Microsoft.JSInterop;

namespace MediaEngine.Web.Services.Integration;

/// <summary>
/// Stores the active local profile for the current browser.
/// </summary>
public sealed class ActiveProfileSessionService
{
    private const string StorageKey = "tuvima-active-profile-id";

    private readonly IJSRuntime _js;
    private Guid? _cachedProfileId;
    private bool _loaded;

    public ActiveProfileSessionService(IJSRuntime js)
    {
        _js = js;
    }

    public async Task<ProfileViewModel?> ResolveAsync(
        IReadOnlyList<ProfileViewModel> profiles,
        CancellationToken ct = default)
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

    public async Task SetActiveProfileAsync(Guid profileId, CancellationToken ct = default)
    {
        _cachedProfileId = profileId;
        _loaded = true;
        await SaveAsync(profileId, ct);
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;

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
}
