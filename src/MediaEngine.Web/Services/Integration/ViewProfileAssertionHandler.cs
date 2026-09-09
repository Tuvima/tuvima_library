namespace MediaEngine.Web.Services.Integration;

/// <summary>
/// Removes obsolete browser-selected profile assertions. The Engine resolves View scope
/// from the forwarded authenticated session and service credential on each request.
/// </summary>
public sealed class ViewProfileAssertionHandler : DelegatingHandler
{
    public const string ProfileHeader = "X-Tuvima-View-Profile";
    public const string TimestampHeader = "X-Tuvima-View-Timestamp";
    public const string SignatureHeader = "X-Tuvima-View-Signature";

    public ViewProfileAssertionHandler(
        IActiveProfileAccessor activeProfile,
        TimeProvider? timeProvider = null)
    {
        _ = activeProfile;
        _ = timeProvider;
    }

    public ViewProfileAssertionHandler(
        IActiveProfileAccessor activeProfile,
        string apiKey,
        TimeProvider? timeProvider = null)
    {
        _ = activeProfile;
        _ = apiKey;
        _ = timeProvider;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        RemoveAssertionHeaders(request);

        return base.SendAsync(request, cancellationToken);
    }

    private static void RemoveAssertionHeaders(HttpRequestMessage request)
    {
        request.Headers.Remove(ProfileHeader);
        request.Headers.Remove(TimestampHeader);
        request.Headers.Remove(SignatureHeader);
    }
}
