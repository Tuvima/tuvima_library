using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using MediaEngine.Contracts.Settings;

namespace MediaEngine.Api.Services.Networking;

public sealed class NetworkEnvironmentService : INetworkEnvironmentService
{
    public IReadOnlyList<NetworkAddressDto> GetUsableAddresses(bool includeIpv6)
    {
        var addresses = new List<NetworkAddressDto>();
        foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (networkInterface.OperationalStatus != OperationalStatus.Up
                || networkInterface.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                continue;

            foreach (var unicast in networkInterface.GetIPProperties().UnicastAddresses)
            {
                var address = unicast.Address;
                if (IPAddress.IsLoopback(address)
                    || address.IsIPv6Multicast
                    || address.IsIPv6LinkLocal
                    || address.IsIPv6SiteLocal
                    || address.Equals(IPAddress.Any)
                    || address.Equals(IPAddress.IPv6Any))
                    continue;
                if (address.AddressFamily == AddressFamily.InterNetworkV6 && !includeIpv6)
                    continue;
                if (address.AddressFamily is not AddressFamily.InterNetwork and not AddressFamily.InterNetworkV6)
                    continue;

                addresses.Add(new NetworkAddressDto
                {
                    InterfaceId = networkInterface.Id,
                    InterfaceName = networkInterface.Name,
                    Address = address.ToString(),
                    AddressFamily = address.AddressFamily == AddressFamily.InterNetwork ? "ipv4" : "ipv6",
                });
            }
        }

        return addresses
            .OrderBy(address => address.AddressFamily == "ipv4" ? 0 : 1)
            .ThenBy(address => address.InterfaceName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(address => address.Address, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
