using System.Net;
using MediaEngine.Domain.Configuration;
using Microsoft.AspNetCore.HttpOverrides;

namespace MediaEngine.Web.Services.Configuration;

public static class ForwardedHeaderConfiguration
{
    public static void Configure(
        ForwardedHeadersOptions options,
        RemoteNetworkSettings remote,
        string? tailscaleUrl)
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor
            | ForwardedHeaders.XForwardedProto
            | ForwardedHeaders.XForwardedHost;
        options.ForwardLimit = 1;
        AddProxy(options, IPAddress.Loopback);

        foreach (var address in remote.TrustedProxies)
        {
            if (IPAddress.TryParse(address, out var proxy))
                AddProxy(options, proxy);
        }

        foreach (var cidr in remote.TrustedProxyNetworks)
        {
            if (System.Net.IPNetwork.TryParse(cidr, out var network)
                && !options.KnownIPNetworks.Contains(network))
            {
                options.KnownIPNetworks.Add(network);
                if (network.BaseAddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                {
                    var mapped = new System.Net.IPNetwork(
                        network.BaseAddress.MapToIPv6(),
                        96 + network.PrefixLength);
                    if (!options.KnownIPNetworks.Contains(mapped))
                        options.KnownIPNetworks.Add(mapped);
                }
            }
        }

        foreach (var value in new[] { remote.PublicHostname, tailscaleUrl })
        {
            if (Uri.TryCreate(value, UriKind.Absolute, out var uri)
                && !options.AllowedHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase))
            {
                options.AllowedHosts.Add(uri.Host);
            }
        }
    }

    private static void AddProxy(ForwardedHeadersOptions options, IPAddress address)
    {
        if (!options.KnownProxies.Contains(address))
            options.KnownProxies.Add(address);
        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            var mapped = address.MapToIPv6();
            if (!options.KnownProxies.Contains(mapped))
                options.KnownProxies.Add(mapped);
        }
    }

    public static bool IsLocalNetworkClient(IPAddress? address)
    {
        if (address is null || IPAddress.IsLoopback(address))
            return true;
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();
        if (address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            return address.IsIPv6LinkLocal || address.IsIPv6SiteLocal;
        var bytes = address.GetAddressBytes();
        return bytes[0] == 10
            || bytes[0] == 127
            || bytes[0] == 192 && bytes[1] == 168
            || bytes[0] == 172 && bytes[1] is >= 16 and <= 31
            || bytes[0] == 169 && bytes[1] == 254;
    }
}
