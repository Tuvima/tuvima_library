namespace MediaEngine.Domain.Enums;

/// <summary>
/// Defines the access level for a user profile.
///
/// <list type="bullet">
///   <item><see cref="Administrator"/> — Full access to all settings, user management, and maintenance.</item>
///   <item><see cref="Curator"/> — Can correct metadata and manage library content, but cannot manage users or system settings.</item>
///   <item><see cref="Consumer"/> — Read-only access to library content and personal preferences.</item>
/// </list>
///
/// Spec: Settings & Management Layer — Identity & Multi-User.
/// </summary>
public enum ProfileRole
{
    /// <summary>Full system access. Can manage users, API keys, library folders, and maintenance tasks.</summary>
    Administrator = 0,

    /// <summary>Can correct metadata and browse the library. Cannot manage system settings or users.</summary>
    Curator = 1,

    /// <summary>Can browse the library and set personal preferences. No management capabilities.</summary>
    Consumer = 2,
}
