using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace MediaEngine.Web.Services.Integration;

/// <summary>
/// Adds a short-lived, profile-bound assertion to Engine View and Collections API requests.
/// Signing happens in the Dashboard server after the final request URI exists.
/// </summary>
public sealed class ViewProfileAssertionHandler : DelegatingHandler
{
    public const string ProfileHeader = "X-Tuvima-View-Profile";
    public const string TimestampHeader = "X-Tuvima-View-Timestamp";
    public const string SignatureHeader = "X-Tuvima-View-Signature";

    private readonly IActiveProfileAccessor _activeProfile;
    private readonly byte[] _keyBytes;
    private readonly TimeProvider _timeProvider;

    public ViewProfileAssertionHandler(
        IActiveProfileAccessor activeProfile,
        string apiKey,
        TimeProvider? timeProvider = null)
    {
        _activeProfile = activeProfile;
        _keyBytes = Encoding.UTF8.GetBytes(apiKey ?? string.Empty);
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        RemoveAssertionHeaders(request);

        if (_activeProfile.ProfileId is { } profileId
            && IsEligibleRequest(request.RequestUri))
        {
            request.Headers.TryAddWithoutValidation(ProfileHeader, profileId.ToString("D"));
            if (_keyBytes.Length == 0)
            {
                return base.SendAsync(request, cancellationToken);
            }

            var timestamp = _timeProvider.GetUtcNow().ToUnixTimeSeconds()
                .ToString(CultureInfo.InvariantCulture);
            var canonicalTarget = CanonicalTarget(request.RequestUri!);
            var canonical = string.Join(
                '\n',
                profileId.ToString("D"),
                timestamp,
                request.Method.Method.ToUpperInvariant(),
                canonicalTarget);
            var signature = Sign(canonical);

            request.Headers.TryAddWithoutValidation(TimestampHeader, timestamp);
            request.Headers.TryAddWithoutValidation(SignatureHeader, signature);
        }

        return base.SendAsync(request, cancellationToken);
    }

    private static bool IsEligibleRequest(Uri? uri)
    {
        if (uri is null)
        {
            return false;
        }

        var path = uri.IsAbsoluteUri ? uri.AbsolutePath : uri.OriginalString.Split('?', 2)[0];
        return IsPathFamily(path, "/view") || IsPathFamily(path, "/collections");
    }

    private static bool IsPathFamily(string path, string family) =>
        path.Equals(family, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith($"{family}/", StringComparison.OrdinalIgnoreCase);

    private static string CanonicalTarget(Uri uri) =>
        uri.IsAbsoluteUri
            ? uri.PathAndQuery
            : uri.OriginalString.StartsWith('/') ? uri.OriginalString : $"/{uri.OriginalString}";

    private string Sign(string canonical)
    {
        var digest = HMACSHA256.HashData(_keyBytes, Encoding.UTF8.GetBytes(canonical));
        return Convert.ToBase64String(digest)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static void RemoveAssertionHeaders(HttpRequestMessage request)
    {
        request.Headers.Remove(ProfileHeader);
        request.Headers.Remove(TimestampHeader);
        request.Headers.Remove(SignatureHeader);
    }
}
