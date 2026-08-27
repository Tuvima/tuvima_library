namespace MediaEngine.Domain.Enums;

/// <summary>
/// Defines the access level for a user profile.
///
/// <list type="bullet">
///   <item><see cref="Administrator"/> — Full access to all settings, user management, and maintenance.</item>
///   <item><see cref="StandardUser"/> — Normal library use without system administration.</item>
///   <item><see cref="RestrictedProfile"/> — Policy-limited library use and profile-local preferences.</item>
/// </list>
///
/// Spec: Settings & Management Layer — Identity & Multi-User.
/// </summary>
public enum ProfileRole
{
    /// <summary>Full system access. Can manage users, API keys, library folders, and maintenance tasks.</summary>
    Administrator = 0,

    /// <summary>Can use the library and manage personal state. Cannot administer the system.</summary>
    StandardUser = 1,

    /// <summary>Can use only capabilities explicitly granted to this restricted profile.</summary>
    RestrictedProfile = 2,
}
