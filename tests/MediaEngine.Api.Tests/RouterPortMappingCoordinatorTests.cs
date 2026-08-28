using MediaEngine.Api.Services.Networking;
using MediaEngine.Contracts.Settings;
using MediaEngine.Domain.Configuration;
using MediaEngine.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaEngine.Api.Tests;

public sealed class RouterPortMappingCoordinatorTests
{
    [Fact]
    public async Task DockerBridgeDoesNotCallAnyRouterProtocol()
    {
        var (loader, path) = CreateSettings();
        try
        {
            var mapper = new FakeMapper("PCP", 10, RouterMappingState.Active);
            var coordinator = new RouterPortMappingCoordinator(
                loader,
                new FakeEnvironment(),
                new FakeTopology(false),
                [mapper],
                new NetworkRuntimeState(),
                NullLogger<RouterPortMappingCoordinator>.Instance);

            var result = await coordinator.EnsureMappingAsync(CancellationToken.None);

            Assert.Equal(RouterMappingState.UnsupportedTopology, result.State);
            Assert.Equal(0, mapper.CreateCalls);
        }
        finally
        {
            Directory.Delete(path, recursive: true);
        }
    }

    [Fact]
    public async Task CoordinatorUsesProtocolPriorityAndTargetsOnlyTlsTerminator()
    {
        var (loader, path) = CreateSettings();
        try
        {
            var order = new List<string>();
            var upnp = new FakeMapper("UPnP", 30, RouterMappingState.Active, order);
            var pcp = new FakeMapper("PCP", 10, RouterMappingState.ProtocolUnavailable, order);
            var natPmp = new FakeMapper("NAT-PMP", 20, RouterMappingState.ProtocolUnavailable, order);
            var coordinator = new RouterPortMappingCoordinator(
                loader,
                new FakeEnvironment(),
                new FakeTopology(true),
                [upnp, natPmp, pcp],
                new NetworkRuntimeState(),
                NullLogger<RouterPortMappingCoordinator>.Instance);

            var result = await coordinator.EnsureMappingAsync(CancellationToken.None);

            Assert.Equal(["PCP", "NAT-PMP", "UPnP"], order);
            Assert.Equal(RouterMappingState.Active, result.State);
            Assert.Equal(8443, upnp.LastRequest!.InternalPort);
            Assert.Equal(443, upnp.LastRequest.ExternalPort);
            Assert.NotEqual(5016, upnp.LastRequest.InternalPort);
        }
        finally
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private static (ConfigurationDirectoryLoader Loader, string Path) CreateSettings()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tuvima-router-coordinator-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        var loader = new ConfigurationDirectoryLoader(path);
        loader.SaveNetwork(new NetworkSettings
        {
            Remote = new RemoteNetworkSettings
            {
                ConnectionMode = NetworkConnectionModes.DirectOnly,
                AutomaticRouterConfiguration = true,
                PublicHostname = "https://library.example.test",
                TlsTerminationPort = 8443,
                ExternalPort = 443,
            },
        });
        return (loader, path);
    }

    private sealed class FakeEnvironment : INetworkEnvironmentService
    {
        public IReadOnlyList<NetworkAddressDto> GetUsableAddresses(bool includeIpv6) =>
        [
            new NetworkAddressDto
            {
                Address = "192.168.1.20",
                AddressFamily = "ipv4",
                InterfaceName = "Ethernet",
                InterfaceId = "test",
            },
        ];
    }

    private sealed class FakeTopology(bool supported) : INetworkTopologyService
    {
        public NetworkTopologySnapshot GetSnapshot() => new(
            supported ? "native" : "docker-bridge",
            supported,
            supported ? "LAN gateway available." : "docker0 is not the LAN gateway.",
            supported ? "192.168.1.1" : "172.18.0.1",
            supported ? "Ethernet" : "eth0");
    }

    private sealed class FakeMapper(
        string method,
        int priority,
        RouterMappingState state,
        List<string>? order = null) : IRouterPortMapper
    {
        public string Method => method;
        public int Priority => priority;
        public int CreateCalls { get; private set; }
        public RouterMappingRequest? LastRequest { get; private set; }

        public Task<RouterMappingResult> TryCreateAsync(RouterMappingRequest request, CancellationToken ct)
        {
            CreateCalls++;
            LastRequest = request;
            order?.Add(Method);
            return Task.FromResult(new RouterMappingResult(state, Method, state.ToString(), request.ExternalPort));
        }

        public Task<RouterMappingResult> TryRenewAsync(RouterMappingRequest request, CancellationToken ct) =>
            TryCreateAsync(request, ct);

        public Task RemoveOwnedAsync(RouterMappingRequest request, CancellationToken ct) => Task.CompletedTask;
    }
}
