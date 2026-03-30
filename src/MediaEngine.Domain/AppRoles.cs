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

    /// <summary>Can correct metadata and manage library content.</summary>
    public const string Curator = nameof(ProfileRole.Curator);

    /// <summary>Read-only access to library content and personal preferences.</summary>
    public const string Consumer = nameof(ProfileRole.Consumer);

    /// <summary>All valid role names for validation.</summary>
    public static readonly string[] All = [Administrator, Curator, Consumer];
}
