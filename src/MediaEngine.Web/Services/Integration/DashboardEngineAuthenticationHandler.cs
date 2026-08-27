using System.Security.Claims;

namespace MediaEngine.Web.Services.Integration;

public sealed class DashboardEngineAuthenticationHandler(
    DashboardServiceCredentialProvider serviceCredential,
    DashboardSessionAccessor session,
    IHttpContextAccessor httpContextAccessor) : DelegatingHandler
{
    public const string ServiceHeader = "X-Tuvima-Service-Key";
    public const string SessionHeader = "X-Tuvima-Session";
    public const string SessionTokenClaim = "tuvima:session_token";

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.Remove(ServiceHeader);
        request.Headers.TryAddWithoutValidation(ServiceHeader, serviceCredential.GetToken());

        var explicitlySuppliedToken = request.Headers.TryGetValues(SessionHeader, out var suppliedValues)
            ? suppliedValues.FirstOrDefault()
            : null;
        request.Headers.Remove(SessionHeader);
        var token = explicitlySuppliedToken
            ?? session.SessionToken
            ?? httpContextAccessor.HttpContext?.User.FindFirstValue(SessionTokenClaim);
        if (!string.IsNullOrWhiteSpace(token))
            request.Headers.TryAddWithoutValidation(SessionHeader, token);

        return base.SendAsync(request, cancellationToken);
    }
}
