using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace MediaEngine.Api.Services.Networking;

public interface IUdpGatewayTransport
{
    Task<byte[]> ExchangeAsync(
        IPAddress gateway,
        int port,
        ReadOnlyMemory<byte> payload,
        TimeSpan timeout,
        CancellationToken ct);
}

public sealed class UdpGatewayTransport : IUdpGatewayTransport
{
    public async Task<byte[]> ExchangeAsync(
        IPAddress gateway,
        int port,
        ReadOnlyMemory<byte> payload,
        TimeSpan timeout,
        CancellationToken ct)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutSource.CancelAfter(timeout);
        using var client = new UdpClient(AddressFamily.InterNetwork);
        await client.SendAsync(payload, new IPEndPoint(gateway, port), timeoutSource.Token);
        return (await client.ReceiveAsync(timeoutSource.Token)).Buffer;
    }
}

public interface IRouterNonceSource
{
    byte[] Create(int length);
}

public sealed class RouterNonceSource : IRouterNonceSource
{
    public byte[] Create(int length) => RandomNumberGenerator.GetBytes(length);
}

public interface IUpnpDiscoveryTransport
{
    Task<IReadOnlyList<Uri>> DiscoverLocationsAsync(string searchTarget, CancellationToken ct);
}

public sealed class UpnpDiscoveryTransport : IUpnpDiscoveryTransport
{
    public async Task<IReadOnlyList<Uri>> DiscoverLocationsAsync(string searchTarget, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        using var client = new UdpClient(AddressFamily.InterNetwork) { EnableBroadcast = true };
        var payload = Encoding.ASCII.GetBytes(
            "M-SEARCH * HTTP/1.1\r\n" +
            "HOST: 239.255.255.250:1900\r\n" +
            "MAN: \"ssdp:discover\"\r\n" +
            "MX: 2\r\n" +
            $"ST: {searchTarget}\r\n\r\n");
        await client.SendAsync(payload, new IPEndPoint(IPAddress.Parse("239.255.255.250"), 1900), timeout.Token);

        var locations = new HashSet<Uri>();
        while (!timeout.IsCancellationRequested)
        {
            try
            {
                var response = Encoding.ASCII.GetString((await client.ReceiveAsync(timeout.Token)).Buffer);
                var locationLine = response.Split("\r\n", StringSplitOptions.RemoveEmptyEntries)
                    .FirstOrDefault(line => line.StartsWith("LOCATION:", StringComparison.OrdinalIgnoreCase));
                if (locationLine is not null
                    && Uri.TryCreate(locationLine[(locationLine.IndexOf(':') + 1)..].Trim(), UriKind.Absolute, out var location)
                    && location.Scheme == Uri.UriSchemeHttp)
                {
                    locations.Add(location);
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                break;
            }
        }

        return locations.ToList();
    }
}
