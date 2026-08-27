using MediaEngine.Domain;
using MediaEngine.Domain.Aggregates;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Enums;

namespace MediaEngine.Api.Services.Details.Internals;

/// <summary>
/// Effective permissions for detail-page actions. Presentation context is
/// deliberately excluded: an admin-looking surface is not authorization.
/// </summary>
internal readonly record struct DetailActionAuthorizationContext(bool CanManageMetadata)
{
    public bool Allows(string actionKey) => actionKey switch
    {
        "edit" => CanManageMetadata,
        _ => false,
    };
}

internal static class DetailActionAuthorizationPolicy
{
    public static async Task<DetailActionAuthorizationContext> ResolveAsync(
        string? callerRole,
        Guid? profileId,
        IProfileRepository? profiles,
        CancellationToken ct)
    {
        var callerCanManage = IsManager(callerRole);
        if (!callerCanManage)
            return new(false);

        // API clients without a profile still use the authenticated API role.
        if (!profileId.HasValue)
            return new(true);

        // A requested but missing profile fails closed. This prevents a
        // privileged dashboard connection from leaking management actions
        // into a Consumer profile or an invalid profile context.
        var profile = profiles is null
            ? null
            : await profiles.GetByIdAsync(profileId.Value, ct).ConfigureAwait(false);
        return new(profile is not null && IsManager(profile));
    }

    private static bool IsManager(string? role) =>
        string.Equals(role, AppRoles.Administrator, StringComparison.OrdinalIgnoreCase)
        || string.Equals(role, AppRoles.StandardUser, StringComparison.OrdinalIgnoreCase);

    private static bool IsManager(Profile profile) =>
        profile.Role is ProfileRole.Administrator or ProfileRole.StandardUser;
}
