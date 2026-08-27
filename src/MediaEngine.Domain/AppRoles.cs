using MediaEngine.Domain.Enums;

namespace MediaEngine.Domain;

/// <summary>
/// String constants for authorization role names, bridging the <see cref="ProfileRole"/>
/// enum to the string-based world of API validation, middleware, and policy filters.
/// </summary>
public static class AppRoles
{
    /// <summary>Full system access.</summary>
    public const string Administrator = nameof(ProfileRole.Administrator);

    /// <summary>Normal library use without system administration.</summary>
    public const string StandardUser = nameof(ProfileRole.StandardUser);

    /// <summary>Policy-limited library use and profile-local preferences.</summary>
    public const string RestrictedProfile = nameof(ProfileRole.RestrictedProfile);

    /// <summary>All valid role names for validation.</summary>
    public static readonly string[] All = [Administrator, StandardUser, RestrictedProfile];
}
