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
            .Where(profile => profile.IncludeInSharedView)
            .ToList();

        var options = BuildOptions(callerState!, shareable);
        var resolved = requested.Kind switch
        {
            ViewScopeKind.Mine => Mine(callerState!),
            ViewScopeKind.Shared when callerState!.AccessSharedView => Shared(shareable),
            ViewScopeKind.Profile when requested.ProfileId == caller.ProfileId => Mine(callerState!),
            ViewScopeKind.Profile when callerState!.AccessSharedView =>
                ResolveProfile(requested.ProfileId, shareable),
            _ => null,
        };

        var fallback = callerState!.AccessSharedView
            ? Shared(shareable) with { WasFallback = true }
            : Mine(callerState, fellBack: true);
        return new ViewScopeResolution(resolved ?? fallback, options);
    }

    private static bool IsUsable(ViewProfileScopeState? profile) =>
        profile is { ViewEnabled: true, PersonalLibraryId: not null };

    private static ResolvedViewScope Mine(ViewProfileScopeState caller, bool fellBack = false) =>
        new(
            ViewScopeKind.Mine,
            caller.ProfileId,
            new HashSet<Guid> { caller.PersonalLibraryId!.Value },
            fellBack);

    private static ResolvedViewScope Shared(IReadOnlyList<ViewProfileScopeState> shareable) =>
        new(
            ViewScopeKind.Shared,
            null,
            shareable.Select(profile => profile.PersonalLibraryId!.Value).ToHashSet());

    private static ResolvedViewScope? ResolveProfile(
        Guid? requestedProfileId,
        IReadOnlyList<ViewProfileScopeState> shareable)
    {
        var profile = shareable.FirstOrDefault(candidate => candidate.ProfileId == requestedProfileId);
        return profile is null
            ? null
            : new ResolvedViewScope(
                ViewScopeKind.Profile,
                profile.ProfileId,
                new HashSet<Guid> { profile.PersonalLibraryId!.Value });
    }

    private static IReadOnlyList<ViewScopeOption> BuildOptions(
        ViewProfileScopeState caller,
        IReadOnlyList<ViewProfileScopeState> shareable)
    {
        var result = new List<ViewScopeOption> { new(ViewScopeKind.Mine, caller.ProfileId) };
        if (!caller.AccessSharedView)
        {
            return result;
        }

        result.Add(new ViewScopeOption(ViewScopeKind.Shared, null));
        result.AddRange(shareable.Where(profile => profile.ProfileId != caller.ProfileId).Select(profile =>
            new ViewScopeOption(ViewScopeKind.Profile, profile.ProfileId)));
        return result;
    }
}
