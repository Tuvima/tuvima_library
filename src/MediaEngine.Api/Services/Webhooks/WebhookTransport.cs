using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace MediaEngine.Api.Services.Webhooks;

internal sealed class WebhookDestinationException : Exception
{
    public WebhookDestinationException() : base("The webhook destination is not permitted.") { }
}

internal interface IWebhookDnsResolver
{
    Task<IPAddress[]> ResolveAsync(string host, CancellationToken ct);
}

internal sealed class WebhookDnsResolver : IWebhookDnsResolver
{
    public Task<IPAddress[]> ResolveAsync(string host, CancellationToken ct) => Dns.GetHostAddressesAsync(host, ct);
}

/// <summary>Validates configuration and pins every outbound connection to a freshly approved address.</summary>
internal sealed class WebhookDestinationPolicy(IWebhookDnsResolver dns)
{
    public static Uri Parse(string value, bool allowLocalNetwork)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 2048 || !Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Fragment) || !string.IsNullOrEmpty(uri.Query)
            || (uri.Scheme == "http" && !allowLocalNetwork))
        {
            throw new WebhookDestinationException();
        }

        return uri;
    }

    public async Task<IPAddress[]> ResolveApprovedAsync(Uri uri, bool allowLocalNetwork, CancellationToken ct)
    {
        IPAddress[] addresses;
        try { addresses = await dns.ResolveAsync(uri.IdnHost, ct); }
        catch (SocketException) { throw new WebhookDestinationException(); }
        if (addresses.Length == 0 || addresses.Any(address => !IsAllowed(address, allowLocalNetwork)))
        {
            throw new WebhookDestinationException();
        }
        // Plain HTTP is only available for an explicitly approved private-network destination.
        if (uri.Scheme == "http" && addresses.Any(address => !IsPrivate(address)))
        {
            throw new WebhookDestinationException();
        }

        return addresses;
    }

    internal static bool IsAllowed(IPAddress address, bool allowLocalNetwork)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any)
            || address.IsIPv6LinkLocal || address.IsIPv6Multicast || address.IsIPv6SiteLocal)
        {
            return false;
        }

        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            if (bytes[0] == 0 || bytes[0] >= 224 || bytes[0] == 127
                || bytes[0] == 169 && bytes[1] == 254
                || bytes[0] == 100 && bytes[1] is >= 64 and <= 127
                || bytes[0] == 198 && bytes[1] is 18 or 19)
            {
                return false;
            }
        }
        else if (address.AddressFamily != AddressFamily.InterNetworkV6 ||
                 !(bytes[0] is >= 0x20 and <= 0x3f || (bytes[0] & 0xfe) == 0xfc))
        {
            return false;
        }

        return allowLocalNetwork || !IsPrivate(address);
    }

    private static bool IsPrivate(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        var bytes = address.GetAddressBytes();
        return address.AddressFamily == AddressFamily.InterNetwork
            ? bytes[0] == 10 || bytes[0] == 172 && bytes[1] is >= 16 and <= 31 || bytes[0] == 192 && bytes[1] == 168
            : (bytes[0] & 0xfe) == 0xfc;
    }

    public SocketsHttpHandler CreateHandler(Uri destination, bool allowLocalNetwork) => new()
    {
        AllowAutoRedirect = false,
        UseProxy = false,
        UseCookies = false,
        ConnectTimeout = TimeSpan.FromSeconds(5),
        MaxResponseHeadersLength = 16,
        ConnectCallback = async (context, ct) =>
        {
            if (!context.DnsEndPoint.Host.Equals(destination.IdnHost, StringComparison.OrdinalIgnoreCase)
                || context.DnsEndPoint.Port != destination.Port)
            {
                throw new WebhookDestinationException();
            }

            var addresses = await ResolveApprovedAsync(destination, allowLocalNetwork, ct);
            foreach (var address in addresses)
            {
                var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                try
                {
                    await socket.ConnectAsync(new IPEndPoint(address, destination.Port), ct);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    if (ct.IsCancellationRequested)
                    {
                        throw;
                    }
                }
            }
            throw new HttpRequestException("The webhook receiver could not be reached.");
        },
    };
}

internal static class WebhookSignature
{
    public static string Sign(string secret, long unixTimestamp, ReadOnlySpan<byte> body)
    {
        var prefix = Encoding.UTF8.GetBytes(unixTimestamp.ToString(CultureInfo.InvariantCulture) + ".");
        var signed = new byte[prefix.Length + body.Length];
        prefix.CopyTo(signed, 0);
        body.CopyTo(signed.AsSpan(prefix.Length));
        return "sha256=" + Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), signed));
    }
}

internal sealed record WebhookSendResult(bool Delivered, string Status, bool Retryable);

internal interface IWebhookTransport
{
    Task<WebhookSendResult> SendAsync(string url, bool allowLocalNetwork, string secret,
        Guid deliveryId, Guid eventId, ReadOnlyMemory<byte> body, CancellationToken ct);
}

internal sealed class WebhookTransport(WebhookDestinationPolicy destinations, TimeProvider clock) : IWebhookTransport
{
    public async Task<WebhookSendResult> SendAsync(string url, bool allowLocalNetwork, string secret,
        Guid deliveryId, Guid eventId, ReadOnlyMemory<byte> body, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            var uri = WebhookDestinationPolicy.Parse(url, allowLocalNetwork);
            using var handler = destinations.CreateHandler(uri, allowLocalNetwork);
            using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
            return await SendRequestAsync(client, uri, secret, deliveryId, eventId, body,
                clock.GetUtcNow().ToUnixTimeSeconds(), timeout.Token);
        }
        catch (WebhookDestinationException) { return new(false, "Destination blocked", false); }
        catch (HttpRequestException ex) when (ex.InnerException is WebhookDestinationException)
        { return new(false, "Destination blocked", false); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return new(false, "Delivery timed out", true); }
        catch (HttpRequestException) { return new(false, "Receiver unavailable", true); }
    }

    internal static async Task<WebhookSendResult> SendRequestAsync(HttpClient client, Uri uri, string secret,
        Guid deliveryId, Guid eventId, ReadOnlyMemory<byte> body, long timestamp, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, uri);
        request.Content = new ByteArrayContent(body.ToArray());
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        request.Headers.Add("Tuvima-Timestamp", timestamp.ToString(CultureInfo.InvariantCulture));
        request.Headers.Add("Tuvima-Signature", WebhookSignature.Sign(secret, timestamp, body.Span));
        request.Headers.Add("Tuvima-Delivery-Id", deliveryId.ToString("D"));
        request.Headers.Add("Tuvima-Event-Id", eventId.ToString("D"));
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        var code = (int)response.StatusCode;
        return new(response.IsSuccessStatusCode, $"HTTP {code}", code is 408 or 429 || code >= 500);
    }
}
