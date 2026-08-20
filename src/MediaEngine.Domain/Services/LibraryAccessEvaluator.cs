using MediaEngine.Domain.Configuration;

namespace MediaEngine.Domain.Services;

public enum LibraryAccessAction
{
    Read,
    Contribute,
    Manage,
}

/// <summary>Caller identity used for one library authorization decision.</summary>
public sealed record LibraryAccessSubject(Guid ProfileId, string Role);

/// <summary>
/// A normalized personal-library access policy. API and storage callers map
/// configuration into this shape and use the same evaluator for browse, search,
/// thumbnails, originals, uploads, and management.
/// </summary>
public sealed record LibraryAccessPolicy
{
    public required Guid OwnerProfileId { get; init; }

    public string Visibility { get; init; } = LibraryVisibility.Private;

    public IReadOnlySet<Guid> AuthorizedProfileIds { get; init; } = new HashSet<Guid>();

    public bool AllowAdministratorAccess { get; init; } = true;
}

public interface ILibraryAccessEvaluator
{
    bool IsAllowed(
        LibraryAccessSubject subject,
        LibraryAccessPolicy policy,
        LibraryAccessAction action);
}

/// <summary>Canonical personal-library authorization rules.</summary>
public sealed class LibraryAccessEvaluator : ILibraryAccessEvaluator
{
    public bool IsAllowed(
        LibraryAccessSubject subject,
        LibraryAccessPolicy policy,
        LibraryAccessAction action)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(policy);

        if (subject.ProfileId == policy.OwnerProfileId)
            return true;

        if (policy.AllowAdministratorAccess
            && string.Equals(subject.Role, AppRoles.Administrator, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var explicitlyAuthorized = policy.AuthorizedProfileIds.Contains(subject.ProfileId);

        return action switch
        {
            LibraryAccessAction.Read => policy.Visibility switch
            {
                LibraryVisibility.Household => true,
                LibraryVisibility.Shared => explicitlyAuthorized,
                _ => false,
            },
            LibraryAccessAction.Contribute => explicitlyAuthorized,
            LibraryAccessAction.Manage => false,
            _ => false,
        };
    }
}
