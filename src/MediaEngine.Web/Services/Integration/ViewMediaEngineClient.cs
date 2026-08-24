namespace MediaEngine.Web.Services.Integration;

public interface IViewMediaEngineClient
{
    Task<HttpResponseMessage> SendAsync(
        ViewMediaGrant grant,
        HttpMethod method,
        string? range,
        string? ifRange,
        CancellationToken cancellationToken);
}

/// <summary>
/// Keeps the temporary legacy Engine media route entirely behind the Dashboard proxy.
/// </summary>
public sealed class ViewMediaEngineClient(HttpClient http) : IViewMediaEngineClient, IDisposable
{
    public Task<HttpResponseMessage> SendAsync(
        ViewMediaGrant grant,
        HttpMethod method,
        string? range,
        string? ifRange,
        CancellationToken cancellationToken)
    {
        var resource = grant.ResourceKind switch
        {
            ViewMediaResourceKind.Thumbnail => "thumbnail",
            ViewMediaResourceKind.Content => "content",
            _ => throw new ArgumentOutOfRangeException(nameof(grant), "Unsupported View media resource kind."),
        };
        var path = $"/view/{grant.LibraryId:D}/items/{grant.AssetId:D}/{resource}?profileId={grant.ProfileId:D}";
        var request = new HttpRequestMessage(method, path);
        AddHeader(request, "Range", range);
        AddHeader(request, "If-Range", ifRange);
        return http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    public void Dispose() => http.Dispose();

    private static void AddHeader(HttpRequestMessage request, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            request.Headers.TryAddWithoutValidation(name, value);
        }
    }
}
