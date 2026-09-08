using MediaEngine.Domain.PersonalMedia;

namespace MediaEngine.Api.Services.View;

/// <summary>
/// Resolves friendly View scopes to authorized physical libraries. A revoked or
/// stale saved scope falls back to Mine without revealing whether the requested
/// profile still exists. Shared is the first fallback when the caller retains
/// Shared View access; Mine is the safe fallback otherwise.
/// </summary>
public sealed class ViewScopeResolver(IViewScopeStore store) : IViewScopeResolver
{
    public async Task<ViewScopeResolution?> ResolveAsync(
        ViewRequestProfile caller,
        ViewScopeRequest requested,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(requested);

        var callerState = await store.FindProfileAsync(caller.ProfileId, ct).ConfigureAwait(false);
        if (!IsUsable(callerState))
        {
            return null;
        }

        var profiles = await store.GetProfilesAsync(ct).ConfigureAwait(false);
        var enabledProfiles = profiles.Where(IsUsable).ToList();

        var options = BuildOptions(callerState!);
        var resolved = requested.Kind switch
        {
            ViewScopeKind.Mine => Mine(callerState!),
            ViewScopeKind.Shared when callerState!.Policy.AccessSharedLibrary => Shared(enabledProfiles),
            ViewScopeKind.Profile when requested.ProfileId == caller.ProfileId => Mine(callerState!),
            _ => null,
        };

        var fallback = Mine(callerState!, fellBack: true);
        return new ViewScopeResolution(resolved ?? fallback, options);
    }

    private static bool IsUsable(ViewScopeStoreEntry? profile) =>
        profile is { Policy.ViewEnabled: true, PersonalSpace: not null };

    private static ResolvedViewScope Mine(ViewScopeStoreEntry caller, bool fellBack = false) =>
        new(
            ViewScopeKind.Mine,
            caller.Policy.ProfileId,
            new HashSet<Guid> { caller.PersonalSpace!.LibraryId },
            fellBack);

    private static ResolvedViewScope Shared(IReadOnlyList<ViewScopeStoreEntry> _) =>
        new(
            ViewScopeKind.Shared,
            null,
            new HashSet<Guid>());

    private static IReadOnlyList<ViewScopeOption> BuildOptions(ViewScopeStoreEntry caller)
    {
        var result = new List<ViewScopeOption>
        {
            new(ViewScopeKind.Mine, caller.Policy.ProfileId,
                string.IsNullOrWhiteSpace(caller.DisplayName) ? "Mine" : caller.DisplayName,
                caller.AvatarColor, caller.AvatarUrl),
        };
        if (!caller.Policy.AccessSharedLibrary)
        {
            return result;
        }

        result.Add(new ViewScopeOption(ViewScopeKind.Shared, null, "Shared Library"));
        return result;
    }
}
