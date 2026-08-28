using System.Net;
using MediaEngine.Api.Services.Networking;

namespace MediaEngine.Api.Tests;

public sealed class NetworkTopologyServiceTests
{
    [Fact]
    public void DockerBridgeIsUnsupportedAndExplainsDocker0()
    {
        var service = new NetworkTopologyService(
            new GatewayDiscovery("172.18.0.1", "172.18.0.2", "eth0"),
            new NetworkTopologyProbe(true, "bridge"));

        var result = service.GetSnapshot();

        Assert.Equal("docker-bridge", result.Kind);
        Assert.False(result.SupportsRouterDiscovery);
        Assert.Contains("docker0", result.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("172.18.0.1", result.GatewayAddress);
    }

    [Fact]
    public void DockerHostAndNativeTopologiesCanUseLanGateway()
    {
        var gateways = new GatewayDiscovery("192.168.1.1", "192.168.1.20", "Ethernet");

        Assert.True(new NetworkTopologyService(gateways, new NetworkTopologyProbe(true, "host"))
            .GetSnapshot().SupportsRouterDiscovery);
        Assert.True(new NetworkTopologyService(gateways, new NetworkTopologyProbe(false, null))
            .GetSnapshot().SupportsRouterDiscovery);
    }

    [Fact]
    public void UndeclaredContainerFailsClosed()
    {
        var result = new NetworkTopologyService(
            new GatewayDiscovery("10.2.0.1", "10.2.0.8", "custom0"),
            new NetworkTopologyProbe(true, null)).GetSnapshot();

        Assert.Equal("container-unknown", result.Kind);
        Assert.False(result.SupportsRouterDiscovery);
        Assert.Contains("not attempted", result.Detail, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class GatewayDiscovery(string gateway, string internalAddress, string interfaceName) : IGatewayDiscoveryService
    {
        public IReadOnlyList<GatewayCandidate> GetIpv4Gateways() =>
        [
            new(IPAddress.Parse(gateway), IPAddress.Parse(internalAddress), interfaceName),
        ];
    }
}
