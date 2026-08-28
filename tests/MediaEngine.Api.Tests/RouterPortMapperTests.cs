using System.Buffers.Binary;
using System.Net;
using System.Net.Http;
using System.Text;
using MediaEngine.Api.Services.Networking;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaEngine.Api.Tests;

public sealed class RouterPortMapperTests
{
    private static readonly GatewayCandidate Gateway = new(
        IPAddress.Parse("192.168.1.1"),
        IPAddress.Parse("192.168.1.20"),
        "Ethernet");

    private static readonly RouterMappingRequest Request = new(
        "Tuvima test",
        "192.168.1.20",
        443,
        443,
        TimeSpan.FromHours(1));

    [Fact]
    public async Task Pcp_EncodesMapRequestAndReturnsActiveLease()
    {
        var nonce = Enumerable.Range(1, 12).Select(value => (byte)value).ToArray();
        var transport = new FakeUdpTransport((_, port, payload) =>
        {
            Assert.Equal(5351, port);
            Assert.Equal(2, payload[0]);
            Assert.Equal(1, payload[1]);
            Assert.Equal((ushort)443, BinaryPrimitives.ReadUInt16BigEndian(payload.AsSpan(40, 2)));
            Assert.Equal((ushort)443, BinaryPrimitives.ReadUInt16BigEndian(payload.AsSpan(42, 2)));
            Assert.Equal(nonce, payload.AsSpan(24, 12).ToArray());

            var response = new byte[60];
            response[0] = 2;
            response[1] = 0x81;
            BinaryPrimitives.WriteUInt32BigEndian(response.AsSpan(4, 4), 3600);
            nonce.CopyTo(response, 24);
            BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(42, 2), 443);
            response[54] = 0xff;
            response[55] = 0xff;
            IPAddress.Parse("198.51.100.9").GetAddressBytes().CopyTo(response, 56);
            return response;
        });
        var mapper = new PcpRouterPortMapper(
            new FakeGatewayDiscovery(),
            transport,
            new FixedNonceSource(nonce),
            NullLogger<PcpRouterPortMapper>.Instance);

        var result = await mapper.TryCreateAsync(Request, CancellationToken.None);

        Assert.Equal(RouterMappingState.Active, result.State);
        Assert.Equal(443, result.ExternalPort);
        Assert.Equal("198.51.100.9", result.PublicAddress);
        Assert.NotNull(result.ExpiresAt);
    }

    [Fact]
    public async Task Pcp_ReportsRouterRefusalAndRejectsMismatchedNonce()
    {
        var nonce = new byte[12];
        var refusal = new FakeUdpTransport((_, _, _) =>
        {
            var response = new byte[60];
            response[0] = 2;
            response[1] = 0x81;
            response[3] = 7;
            return response;
        });
        var refusedMapper = new PcpRouterPortMapper(
            new FakeGatewayDiscovery(), refusal, new FixedNonceSource(nonce), NullLogger<PcpRouterPortMapper>.Instance);

        var refused = await refusedMapper.TryCreateAsync(Request, CancellationToken.None);

        Assert.Equal(RouterMappingState.RouterRefused, refused.State);
        Assert.Equal("pcp-result-7", refused.ReasonCode);

        var mismatch = new FakeUdpTransport((_, _, _) =>
        {
            var response = new byte[60];
            response[0] = 2;
            response[1] = 0x81;
            response[24] = 99;
            return response;
        });
        var mismatchMapper = new PcpRouterPortMapper(
            new FakeGatewayDiscovery(), mismatch, new FixedNonceSource(nonce), NullLogger<PcpRouterPortMapper>.Instance);

        Assert.Equal(
            RouterMappingState.ProtocolUnavailable,
            (await mismatchMapper.TryCreateAsync(Request, CancellationToken.None)).State);
    }

    [Fact]
    public async Task NatPmp_RequestsPublicAddressAndCreatesAndDeletesMapping()
    {
        var calls = 0;
        var transport = new FakeUdpTransport((_, port, payload) =>
        {
            Assert.Equal(5351, port);
            calls++;
            if (payload.Length == 2)
            {
                var publicResponse = new byte[12];
                publicResponse[1] = 128;
                IPAddress.Parse("203.0.113.8").GetAddressBytes().CopyTo(publicResponse, 8);
                return publicResponse;
            }

            Assert.Equal(2, payload[1]);
            Assert.Equal((ushort)443, BinaryPrimitives.ReadUInt16BigEndian(payload.AsSpan(4, 2)));
            var response = new byte[16];
            response[1] = 130;
            BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(10, 2), 443);
            BinaryPrimitives.WriteUInt32BigEndian(response.AsSpan(12, 4),
                BinaryPrimitives.ReadUInt32BigEndian(payload.AsSpan(8, 4)));
            return response;
        });
        var mapper = new NatPmpRouterPortMapper(
            new FakeGatewayDiscovery(), transport, NullLogger<NatPmpRouterPortMapper>.Instance);

        var created = await mapper.TryCreateAsync(Request, CancellationToken.None);
        await mapper.RemoveOwnedAsync(Request, CancellationToken.None);

        Assert.Equal(RouterMappingState.Active, created.State);
        Assert.Equal("203.0.113.8", created.PublicAddress);
        Assert.Equal(4, calls);
    }

    [Fact]
    public async Task NatPmp_TranslatesRouterRefusal()
    {
        var transport = new FakeUdpTransport((_, _, payload) =>
        {
            if (payload.Length == 2)
                return new byte[12] { 0, 128, 0, 0, 0, 0, 0, 0, 203, 0, 113, 8 };
            var response = new byte[16];
            response[1] = 130;
            BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(2, 2), 2);
            return response;
        });
        var mapper = new NatPmpRouterPortMapper(
            new FakeGatewayDiscovery(), transport, NullLogger<NatPmpRouterPortMapper>.Instance);

        var result = await mapper.TryCreateAsync(Request, CancellationToken.None);

        Assert.Equal(RouterMappingState.RouterRefused, result.State);
        Assert.Equal("nat-pmp-result-2", result.ReasonCode);
    }

    [Fact]
    public async Task Upnp_DiscoversServiceCreatesMappingReadsAddressAndDeletesOwnedMapping()
    {
        var actions = new List<string>();
        using var http = new HttpClient(new StubHandler(async request =>
        {
            if (request.Method == HttpMethod.Get)
            {
                return Text("""
                    <root><device><serviceList><service>
                    <serviceType>urn:schemas-upnp-org:service:WANIPConnection:1</serviceType>
                    <controlURL>/control</controlURL>
                    </service></serviceList></device></root>
                    """);
            }

            var action = request.Headers.GetValues("SOAPAction").Single();
            actions.Add(action);
            var body = await request.Content!.ReadAsStringAsync();
            if (action.Contains("AddPortMapping", StringComparison.Ordinal))
            {
                Assert.Contains("<NewInternalPort>443</NewInternalPort>", body, StringComparison.Ordinal);
                Assert.Contains("<NewLeaseDuration>3600</NewLeaseDuration>", body, StringComparison.Ordinal);
                return new HttpResponseMessage(HttpStatusCode.OK);
            }

            if (action.Contains("GetExternalIPAddress", StringComparison.Ordinal))
                return Text("<Envelope><NewExternalIPAddress>198.51.100.20</NewExternalIPAddress></Envelope>");
            return new HttpResponseMessage(HttpStatusCode.OK);
        }));
        var mapper = new UpnpRouterPortMapper(
            http,
            new FakeUpnpDiscovery(new Uri("http://192.168.1.1/device.xml")),
            NullLogger<UpnpRouterPortMapper>.Instance);

        var created = await mapper.TryCreateAsync(Request, CancellationToken.None);
        await mapper.RemoveOwnedAsync(Request, CancellationToken.None);

        Assert.Equal(RouterMappingState.Active, created.State);
        Assert.Equal("198.51.100.20", created.PublicAddress);
        Assert.Contains(actions, action => action.Contains("AddPortMapping", StringComparison.Ordinal));
        Assert.Contains(actions, action => action.Contains("DeletePortMapping", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Upnp_DistinguishesUnavailableDiscoveryFromRouterRefusal()
    {
        using var unusedHttp = new HttpClient(new StubHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK))));
        var unavailableMapper = new UpnpRouterPortMapper(
            unusedHttp, new FakeUpnpDiscovery(), NullLogger<UpnpRouterPortMapper>.Instance);
        Assert.Equal(
            RouterMappingState.ProtocolUnavailable,
            (await unavailableMapper.TryCreateAsync(Request, CancellationToken.None)).State);

        using var refusalHttp = new HttpClient(new StubHandler(request => Task.FromResult(
            request.Method == HttpMethod.Get
                ? Text("<root><service><serviceType>urn:schemas-upnp-org:service:WANIPConnection:1</serviceType><controlURL>/control</controlURL></service></root>")
                : new HttpResponseMessage(HttpStatusCode.Conflict))));
        var refusalMapper = new UpnpRouterPortMapper(
            refusalHttp,
            new FakeUpnpDiscovery(new Uri("http://192.168.1.1/device.xml")),
            NullLogger<UpnpRouterPortMapper>.Instance);

        Assert.Equal(
            RouterMappingState.RouterRefused,
            (await refusalMapper.TryCreateAsync(Request, CancellationToken.None)).State);
    }

    private static HttpResponseMessage Text(string value) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(value, Encoding.UTF8, "text/xml"),
    };

    private sealed class FakeGatewayDiscovery : IGatewayDiscoveryService
    {
        public IReadOnlyList<GatewayCandidate> GetIpv4Gateways() => [Gateway];
    }

    private sealed class FakeUdpTransport(Func<IPAddress, int, byte[], byte[]> respond) : IUdpGatewayTransport
    {
        public Task<byte[]> ExchangeAsync(IPAddress gateway, int port, ReadOnlyMemory<byte> payload, TimeSpan timeout, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(respond(gateway, port, payload.ToArray()));
        }
    }

    private sealed class FixedNonceSource(byte[] nonce) : IRouterNonceSource
    {
        public byte[] Create(int length)
        {
            Assert.Equal(length, nonce.Length);
            return [.. nonce];
        }
    }

    private sealed class FakeUpnpDiscovery(params Uri[] locations) : IUpnpDiscoveryTransport
    {
        public Task<IReadOnlyList<Uri>> DiscoverLocationsAsync(string searchTarget, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Uri>>(locations);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request);
    }
}
