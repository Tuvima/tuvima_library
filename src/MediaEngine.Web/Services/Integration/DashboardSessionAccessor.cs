namespace MediaEngine.Web.Services.Integration;

public sealed class DashboardSessionAccessor
{
    public string? SessionToken { get; private set; }
    public Guid? ProfileId { get; private set; }
    public Guid? ActiveProfileId { get; private set; }
    public Guid? SessionId { get; private set; }
    public string? Role { get; private set; }

    public void Set(string? token, Guid? profileId, Guid? activeProfileId, Guid? sessionId, string? role)
    {
        SessionToken = token;
        ProfileId = profileId;
        ActiveProfileId = activeProfileId;
        SessionId = sessionId;
        Role = role;
    }
}
