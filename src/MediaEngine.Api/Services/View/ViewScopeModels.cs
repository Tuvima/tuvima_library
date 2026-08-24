using MediaEngine.Domain.PersonalMedia;

namespace MediaEngine.Api.Services.View;

/// <summary>
/// A user-facing View scope. Physical library identifiers are deliberately not
/// part of this contract; they are resolved only after the caller is trusted.
/// </summary>
public sealed record ViewScopeRequest(ViewScopeKind Kind, Guid? ProfileId = null)
{
    public static ViewScopeRequest Shared { get; } = new(ViewScopeKind.Shared);
    public static ViewScopeRequest Mine { get; } = new(ViewScopeKind.Mine);
    public static ViewScopeRequest ForProfile(Guid profileId) =>
        new(ViewScopeKind.Profile, profileId);
}

/// <summary>Profile identity established by trusted Engine middleware.</summary>
public sealed record ViewRequestProfile(Guid ProfileId, string Role);

/// <summary>
/// Persistence projection used by scope resolution. AccessSharedView controls
/// what the profile may see; IncludeInSharedView independently controls whether
/// this profile contributes content to Shared View.
/// </summary>
public sealed record ViewScopeStoreEntry(
    ViewProfilePolicy Policy,
    ViewPersonalSpace? PersonalSpace);

public sealed record ResolvedViewScope(
    ViewScopeKind Kind,
    Guid? ProfileId,
    IReadOnlySet<Guid> LibraryIds,
    bool WasFallback = false)
{
    public bool ContainsLibrary(Guid libraryId) => LibraryIds.Contains(libraryId);
}

public sealed record ViewScopeOption(ViewScopeKind Kind, Guid? ProfileId);

public sealed record ViewScopeResolution(
    ResolvedViewScope Scope,
    IReadOnlyList<ViewScopeOption> AvailableScopes);
