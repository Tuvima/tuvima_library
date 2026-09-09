using System.Net;

namespace MediaEngine.Web.Services.Integration;

/// <summary>
/// Adds the server-held Dashboard credential and blocks the upstream request when
/// that credential is unavailable. This keeps authentication fail-closed without
/// turning a temporary startup or credential-rotation race into a Blazor circuit failure.
/// </summary>
public class DashboardServiceCredentialHandler(
    DashboardServiceCredentialProvider serviceCredential) : DelegatingHandler
{
    public const string ServiceHeader = "X-Tuvima-Service-Key";

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        request.Headers.Remove(ServiceHeader);
        if (!serviceCredential.TryGetToken(out var token))
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                RequestMessage = request,
                ReasonPhrase = "Dashboard Engine credential unavailable",
                Content = new StringContent("The Engine connection is temporarily unavailable."),
            });
        }

        request.Headers.TryAddWithoutValidation(ServiceHeader, token);
        return base.SendAsync(request, cancellationToken);
    }
}
