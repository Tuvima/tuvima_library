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
        var shareable = profiles
            .Where(IsUsable)
            .Where(profile => profile.Policy.IncludeInSharedView)
            .ToList();

        var options = BuildOptions(callerState!, shareable);
        var resolved = requested.Kind switch
        {
            ViewScopeKind.Mine => Mine(callerState!),
            ViewScopeKind.Shared when callerState!.Policy.AccessSharedView => Shared(shareable),
            ViewScopeKind.Profile when requested.ProfileId == caller.ProfileId => Mine(callerState!),
            ViewScopeKind.Profile when callerState!.Policy.AccessSharedView =>
                ResolveProfile(requested.ProfileId, shareable),
            _ => null,
        };

        var fallback = callerState!.Policy.AccessSharedView
            ? Shared(shareable) with { WasFallback = true }
            : Mine(callerState, fellBack: true);
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

    private static ResolvedViewScope Shared(IReadOnlyList<ViewScopeStoreEntry> shareable) =>
        new(
            ViewScopeKind.Shared,
            null,
            shareable.Select(profile => profile.PersonalSpace!.LibraryId).ToHashSet());

    private static ResolvedViewScope? ResolveProfile(
        Guid? requestedProfileId,
        IReadOnlyList<ViewScopeStoreEntry> shareable)
    {
        var profile = shareable.FirstOrDefault(candidate => candidate.Policy.ProfileId == requestedProfileId);
        return profile is null
            ? null
            : new ResolvedViewScope(
                ViewScopeKind.Profile,
                profile.Policy.ProfileId,
                new HashSet<Guid> { profile.PersonalSpace!.LibraryId });
    }

    private static IReadOnlyList<ViewScopeOption> BuildOptions(
        ViewScopeStoreEntry caller,
        IReadOnlyList<ViewScopeStoreEntry> shareable)
    {
        var result = new List<ViewScopeOption>
        {
            new(ViewScopeKind.Mine, caller.Policy.ProfileId,
                string.IsNullOrWhiteSpace(caller.DisplayName) ? "Mine" : caller.DisplayName,
                caller.AvatarColor, caller.AvatarUrl),
        };
        if (!caller.Policy.AccessSharedView)
        {
            return result;
        }

        result.Add(new ViewScopeOption(ViewScopeKind.Shared, null, "Shared View"));
        result.AddRange(shareable.Where(profile => profile.Policy.ProfileId != caller.Policy.ProfileId).Select(profile =>
            new ViewScopeOption(ViewScopeKind.Profile, profile.Policy.ProfileId,
                profile.DisplayName, profile.AvatarColor, profile.AvatarUrl)));
        return result;
    }
}
