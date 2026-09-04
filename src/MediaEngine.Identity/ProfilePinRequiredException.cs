namespace MediaEngine.Identity;

/// <summary>
/// Signals that an otherwise-authorized profile switch needs the target
/// profile's PIN before the active session can be updated.
/// </summary>
public sealed class ProfilePinRequiredException : UnauthorizedAccessException
{
    public ProfilePinRequiredException()
        : base("That profile requires its PIN.")
    {
    }
}
