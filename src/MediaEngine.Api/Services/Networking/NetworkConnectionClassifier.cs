using System.Net;
using System.Net.Sockets;
using MediaEngine.Contracts.Playback;

namespace MediaEngine.Api.Services.Networking;

/// <summary>
/// Classifies request topology for playback tuning only. The result must not be
/// used to grant roles, bypass authentication, or make any other trust decision.
/// </summary>
public sealed class NetworkConnectionClassifier
{
    public PlaybackConnectionContextDto Classify(
        HttpContext context,
        string? requestedPath,
        string? provider,
        double? bandwidthMbps,
        int? latencyMs,
        Guid? roomId)
    {
        var path = PlaybackConnectionPaths.IsKnown(requestedPath)
            ? requestedPath!
            : IsLocal(context.Connection.RemoteIpAddress)
                ? PlaybackConnectionPaths.Local
                : PlaybackConnectionPaths.RemoteDirect;

        if (path != PlaybackConnectionPaths.Local && !string.IsNullOrWhiteSpace(provider))
        {
            path = PlaybackConnectionPaths.RemoteSecureProvider;
        }

        return new PlaybackConnectionContextDto
        {
            ConnectionPath = path,
            RemoteConnectivityProvider = string.IsNullOrWhiteSpace(provider) ? null : provider.Trim(),
            EstimatedBandwidthMbps = bandwidthMbps is > 0 ? Math.Min(bandwidthMbps.Value, 100_000) : null,
            LatencyMs = latencyMs is >= 0 ? Math.Min(latencyMs.Value, 120_000) : null,
            RoomId = roomId,
        };
    }

    private static bool IsLocal(IPAddress? address)
    {
        if (address is null || IPAddress.IsLoopback(address))
        {
            return true;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            return bytes[0] == 10
                || bytes[0] == 127
                || bytes[0] == 169 && bytes[1] == 254
                || bytes[0] == 172 && bytes[1] is >= 16 and <= 31
                || bytes[0] == 192 && bytes[1] == 168;
        }

        return address.IsIPv6LinkLocal || address.IsIPv6SiteLocal
            || (address.GetAddressBytes()[0] & 0xfe) == 0xfc;
    }
}
