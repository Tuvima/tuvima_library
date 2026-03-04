namespace MediaEngine.Api.Security;

/// <summary>
/// Validates user-submitted filesystem paths to prevent directory traversal attacks.
///
/// This is a secondary defence — <see cref="RoleAuthorizationFilter"/> ensures only
/// Administrators can call folder-related endpoints in the first place.  This class
/// adds an extra layer of path-level validation as defence-in-depth.
/// </summary>
public static class PathValidator
{
    /// <summary>
    /// Common system paths that should never be specified as a Watch Folder or Library Root.
    /// </summary>
    private static readonly string[] ForbiddenPrefixes = OperatingSystem.IsWindows()
        ? [@"C:\Windows", @"C:\Program Files", @"C:\Program Files (x86)", @"C:\ProgramData"]
        : ["/etc", "/usr", "/bin", "/sbin", "/boot", "/proc", "/sys", "/dev"];

    /// <summary>
    /// Returns <see langword="null"/> if the path is safe, or an error message if it is rejected.
    /// </summary>
    public static string? Validate(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "Path must not be empty.";

        // Reject path traversal sequences — check both separator styles for portability.
        if (path.Contains("..\\") || path.Contains("../") || path.EndsWith(".."))
            return "Path must not contain '..' traversal segments.";

        // Reject known system paths.
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        foreach (var prefix in ForbiddenPrefixes)
        {
            if (path.StartsWith(prefix, comparison))
                return $"Path must not target a system directory ({prefix}).";
        }

        return null; // safe
    }
}
