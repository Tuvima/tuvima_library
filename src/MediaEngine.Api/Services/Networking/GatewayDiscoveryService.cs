using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace MediaEngine.Api.Services.Networking;

public sealed class GatewayDiscoveryService : IGatewayDiscoveryService
{
    public IReadOnlyList<GatewayCandidate> GetIpv4Gateways()
    {
        var results = new List<GatewayCandidate>();
        foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (networkInterface.OperationalStatus != OperationalStatus.Up
                || networkInterface.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                continue;

            var properties = networkInterface.GetIPProperties();
            var internalAddress = properties.UnicastAddresses
                .Select(value => value.Address)
                .FirstOrDefault(address => address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address));
            if (internalAddress is null)
                continue;

            foreach (var gateway in properties.GatewayAddresses.Select(value => value.Address)
                         .Where(address => address.AddressFamily == AddressFamily.InterNetwork && !address.Equals(IPAddress.Any)))
            {
                results.Add(new GatewayCandidate(gateway, internalAddress, networkInterface.Name));
            }
        }

        return results;
    }
}
