using System.Security.Claims;

namespace MediaEngine.Web.Services.Integration;

public sealed class DashboardEngineAuthenticationHandler(
    DashboardServiceCredentialProvider serviceCredential,
    DashboardSessionAccessor session,
    IHttpContextAccessor httpContextAccessor) : DashboardServiceCredentialHandler(serviceCredential)
{
    public new const string ServiceHeader = DashboardServiceCredentialHandler.ServiceHeader;
    public const string SessionHeader = "X-Tuvima-Session";
    public const string SessionTokenClaim = "tuvima:session_token";
    public static readonly HttpRequestOptionsKey<bool> SuppressSessionToken =
        new("Tuvima.SuppressDashboardSessionToken");

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Options.TryGetValue(SuppressSessionToken, out var suppressSessionToken)
            && suppressSessionToken)
        {
            request.Headers.Remove(SessionHeader);
            return base.SendAsync(request, cancellationToken);
        }

        var explicitlySuppliedToken = request.Headers.TryGetValues(SessionHeader, out var suppliedValues)
            ? suppliedValues.FirstOrDefault()
            : null;
        request.Headers.Remove(SessionHeader);
        var forwardingState = session.CurrentForwardingState();
        var token = explicitlySuppliedToken ?? forwardingState.SessionToken;
        if (string.IsNullOrWhiteSpace(token) && !forwardingState.HasEstablishedSessionState)
        {
            token = httpContextAccessor.HttpContext?.User.FindFirstValue(SessionTokenClaim);
        }
        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.TryAddWithoutValidation(SessionHeader, token);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
