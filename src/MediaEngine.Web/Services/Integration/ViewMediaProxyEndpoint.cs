namespace MediaEngine.Web.Services.Integration;

public static class ViewMediaProxyEndpoint
{
    public static IEndpointConventionBuilder MapViewMediaProxy(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapMethods(
                "/view-media/{grant}",
                [HttpMethods.Get, HttpMethods.Head],
                HandleAsync)
            .WithName("ProxyViewMedia")
            .WithSummary("Streams one profile-bound View media resource through the Dashboard origin.");

    public static async Task HandleAsync(
        string grant,
        HttpContext context,
        ViewMediaGrantService grants,
        ActiveProfileAccessor activeProfile,
        IViewMediaEngineClient engine,
        CancellationToken cancellationToken)
    {
        SetPrivateNoStore(context.Response);
        if (!grants.TryValidate(grant, out var mediaGrant) || mediaGrant is null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        activeProfile.SetProfile(mediaGrant.ProfileId);
        var method = HttpMethods.IsHead(context.Request.Method) ? HttpMethod.Head : HttpMethod.Get;
        using var response = await engine.SendAsync(
            mediaGrant,
            method,
            context.Request.Headers.Range.ToString(),
            context.Request.Headers.IfRange.ToString(),
            cancellationToken);

        context.Response.StatusCode = (int)response.StatusCode;
        CopyResponseHeaders(response, context.Response);
        SetPrivateNoStore(context.Response);
        if (!HttpMethods.IsHead(context.Request.Method))
        {
            await response.Content.CopyToAsync(context.Response.Body, cancellationToken);
        }
    }

    private static void SetPrivateNoStore(HttpResponse response)
    {
        response.Headers.CacheControl = "private, no-store";
        response.Headers.Pragma = "no-cache";
        response.Headers.Expires = "0";
    }

    private static void CopyResponseHeaders(HttpResponseMessage source, HttpResponse target)
    {
        foreach (var header in source.Headers)
        {
            target.Headers[header.Key] = header.Value.ToArray();
        }

        foreach (var header in source.Content.Headers)
        {
            target.Headers[header.Key] = header.Value.ToArray();
        }

        target.Headers.Remove("transfer-encoding");
        target.Headers.Remove("set-cookie");
    }
}
