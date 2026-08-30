using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Encodings.Web;
using System.Text;
using MediaEngine.Contracts.Authentication;
using Microsoft.AspNetCore.Antiforgery;

namespace MediaEngine.Web.Endpoints;

public static class ClientApiEdgeEndpoints
{
    public static IEndpointRouteBuilder MapClientApiEdge(this IEndpointRouteBuilder app)
    {
        app.MapGet("/.well-known/tuvima", (HttpRequest request) => Results.Ok(new TuvimaDiscoveryResponse
        {
            ServerId = Environment.MachineName,
            ServerName = Environment.MachineName,
            ApiBaseUrl = $"{request.Scheme}://{request.Host}/api/v1",
            VerificationUri = $"{request.Scheme}://{request.Host}/pair",
        }))
        .WithName("DiscoverTuvimaPublicEdge")
        .AllowAnonymous();

        app.MapGet("/pair", async (
            string? user_code,
            HttpContext context,
            IHttpClientFactory clients,
            IAntiforgery antiforgery,
            CancellationToken ct) =>
        {
            var code = user_code?.Trim() ?? string.Empty;
            PairingReviewResponse? review = null;
            if (!string.IsNullOrWhiteSpace(code))
            {
                using var response = await clients.CreateClient("EngineIdentity")
                    .GetAsync($"/api/v1/pairing/review/{Uri.EscapeDataString(code)}", ct);
                if (response.StatusCode == HttpStatusCode.Unauthorized)
                    return Results.Challenge();
                if (response.IsSuccessStatusCode)
                    review = await response.Content.ReadFromJsonAsync<PairingReviewResponse>(cancellationToken: ct);
            }

            var tokens = antiforgery.GetAndStoreTokens(context);
            return Results.Content(PairingPage(code, review, tokens.RequestToken), "text/html; charset=utf-8");
        })
        .WithName("PairClientDevice");

        app.MapPost("/pair", async (
            HttpContext context,
            IHttpClientFactory clients,
            IAntiforgery antiforgery,
            CancellationToken ct) =>
        {
            await antiforgery.ValidateRequestAsync(context);
            var form = await context.Request.ReadFormAsync(ct);
            var code = form["user_code"].ToString();
            var approved = string.Equals(form["decision"], "approve", StringComparison.Ordinal);
            var scopes = form["scope"].Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!).ToArray();
            using var response = await clients.CreateClient("EngineIdentity").PostAsJsonAsync(
                "/api/v1/pairing/decision",
                new PairingDecisionRequest { UserCode = code, Approved = approved, Scopes = scopes },
                ct);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
                return Results.Challenge();
            if (!response.IsSuccessStatusCode)
                return Results.Content(PairingResultPage("Pairing failed", "The code expired or was already used."), "text/html; charset=utf-8", statusCode: 400);

            return Results.Content(PairingResultPage(
                approved ? "Device paired" : "Pairing denied",
                approved ? "The device can now finish signing in. You may close this page." : "The device was not granted access."),
                "text/html; charset=utf-8");
        })
        .WithName("DecideClientDevicePairing");

        app.MapMethods("/api/v1/{**clientPath}",
            [HttpMethods.Get, HttpMethods.Head, HttpMethods.Post, HttpMethods.Put, HttpMethods.Patch, HttpMethods.Delete],
            ProxyClientApiAsync)
        .WithName("ProxyClientApiV1")
        .AllowAnonymous();

        return app;
    }

    private static async Task ProxyClientApiAsync(
        string? clientPath,
        HttpContext context,
        IHttpClientFactory clients,
        CancellationToken ct)
    {
        var path = clientPath?.TrimStart('/') ?? string.Empty;
        var upstreamPath = path.StartsWith("stream/", StringComparison.OrdinalIgnoreCase)
            ? $"/stream/{path["stream/".Length..]}"
            : path.StartsWith("persons/", StringComparison.OrdinalIgnoreCase)
                ? $"/persons/{path["persons/".Length..]}"
                : path.StartsWith("library/portraits/", StringComparison.OrdinalIgnoreCase)
                    ? $"/library/portraits/{path["library/portraits/".Length..]}"
                    : $"/api/v1/{path}";
        using var request = new HttpRequestMessage(new HttpMethod(context.Request.Method), upstreamPath + context.Request.QueryString);

        if (context.Request.ContentLength > 0 || context.Request.Headers.ContainsKey("Transfer-Encoding"))
        {
            request.Content = new StreamContent(context.Request.Body);
            if (MediaTypeHeaderValue.TryParse(context.Request.ContentType, out var contentType))
                request.Content.Headers.ContentType = contentType;
        }

        CopyRequestHeader(context.Request, request, "Authorization");
        CopyRequestHeader(context.Request, request, "Accept");
        CopyRequestHeader(context.Request, request, "Range");
        CopyRequestHeader(context.Request, request, "If-Range");
        CopyRequestHeader(context.Request, request, "If-None-Match");
        request.Headers.TryAddWithoutValidation("X-Forwarded-Proto", context.Request.Scheme);
        request.Headers.TryAddWithoutValidation("X-Forwarded-Host", context.Request.Host.Value);

        using var response = await clients.CreateClient("ClientApiProxy")
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        context.Response.StatusCode = (int)response.StatusCode;
        foreach (var header in response.Headers)
            context.Response.Headers[header.Key] = header.Value.ToArray();
        foreach (var header in response.Content.Headers)
            context.Response.Headers[header.Key] = header.Value.ToArray();
        context.Response.Headers.Remove("transfer-encoding");
        if (HttpMethods.IsHead(context.Request.Method))
            return;

        if (response.Content.Headers.ContentType?.MediaType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true)
        {
            var json = await response.Content.ReadAsStringAsync(ct);
            json = json.Replace("\"/stream/", "\"/api/v1/stream/", StringComparison.Ordinal)
                .Replace("\"/playback/", "\"/api/v1/playback/", StringComparison.Ordinal)
                .Replace("\"/persons/", "\"/api/v1/persons/", StringComparison.Ordinal)
                .Replace("\"/library/portraits/", "\"/api/v1/library/portraits/", StringComparison.Ordinal);
            var bytes = Encoding.UTF8.GetBytes(json);
            context.Response.Headers.ContentLength = bytes.Length;
            await context.Response.Body.WriteAsync(bytes, ct);
            return;
        }

        await response.Content.CopyToAsync(context.Response.Body, ct);
    }

    private static void CopyRequestHeader(HttpRequest source, HttpRequestMessage destination, string name)
    {
        if (source.Headers.TryGetValue(name, out var value))
            destination.Headers.TryAddWithoutValidation(name, value.ToArray());
    }

    private static string PairingPage(string code, PairingReviewResponse? review, string? antiforgeryToken)
    {
        var html = HtmlEncoder.Default;
        var body = review is null
            ? $"""
                <h1>Pair a device</h1>
                <p>Enter the code shown on your television or native client.</p>
                <form method="get"><label>Pairing code <input name="user_code" value="{html.Encode(code)}" autocomplete="one-time-code" required></label><button>Continue</button></form>
                {(!string.IsNullOrWhiteSpace(code) ? "<p class=error>That code is invalid or expired.</p>" : string.Empty)}
                """
            : $"""
                <h1>Pair {html.Encode(review.DeviceName)}</h1>
                <p><strong>{html.Encode(review.ClientName)}</strong> {html.Encode(review.ClientVersion)} identifies as a {html.Encode(review.DeviceClass)} device.</p>
                <p>Grant only the access you expect:</p>
                <form method="post">
                  <input type="hidden" name="__RequestVerificationToken" value="{html.Encode(antiforgeryToken ?? string.Empty)}">
                  <input type="hidden" name="user_code" value="{html.Encode(code)}">
                  <ul>{string.Join(string.Empty, review.RequestedScopes.Select(scope => $"<li><label><input type=checkbox name=scope value=\"{html.Encode(scope)}\" checked> {html.Encode(scope)}</label></li>"))}</ul>
                  <button name="decision" value="approve">Approve device</button>
                  <button class="secondary" name="decision" value="deny">Deny</button>
                </form>
                """;
        return Layout(body);
    }

    private static string PairingResultPage(string title, string message) =>
        Layout($"<h1>{HtmlEncoder.Default.Encode(title)}</h1><p>{HtmlEncoder.Default.Encode(message)}</p>");

    private static string Layout(string body) => """
        <!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width">
        <title>Tuvima Library device pairing</title>
        <style>body{font:16px system-ui;background:#0e0b17;color:#f7f3ff;max-width:42rem;margin:10vh auto;padding:2rem}form{display:grid;gap:1rem}input{font:inherit;padding:.7rem}button{font:inherit;padding:.8rem 1rem;background:#8b5cf6;color:white;border:0;border-radius:.5rem}button.secondary{background:#40384f}li{margin:.5rem 0}.error{color:#fca5a5}</style>
        <main>
        """ + body + """
        </main></html>
        """;
}
