namespace MediaEngine.Web.Services.Integration;

public sealed class DashboardSessionAccessor
{
    public string? SessionToken { get; private set; }
    public Guid? AccountId { get; private set; }
    public Guid? ActiveProfileId { get; private set; }
    public Guid? SessionId { get; private set; }
    public string? Role { get; private set; }

    public void Set(string? token, Guid? accountId, Guid? activeProfileId, Guid? sessionId, string? role)
    {
        SessionToken = token;
        AccountId = accountId;
        ActiveProfileId = activeProfileId;
        SessionId = sessionId;
        Role = role;
    }
}
