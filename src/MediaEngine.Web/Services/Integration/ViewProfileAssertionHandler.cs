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
    private readonly byte[]? _fixedKeyBytes;
    private readonly TimeProvider _timeProvider;

    public ViewProfileAssertionHandler(
        IActiveProfileAccessor activeProfile,
        TimeProvider? timeProvider = null)
    {
        _activeProfile = activeProfile;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public ViewProfileAssertionHandler(
        IActiveProfileAccessor activeProfile,
        string apiKey,
        TimeProvider? timeProvider = null)
    {
        _activeProfile = activeProfile;
        _fixedKeyBytes = Encoding.UTF8.GetBytes(apiKey ?? string.Empty);
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
            var keyBytes = ResolveKeyBytes(request);
            if (keyBytes.Length == 0)
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
            var signature = Sign(keyBytes, canonical);

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

    private byte[] ResolveKeyBytes(HttpRequestMessage request)
    {
        if (_fixedKeyBytes is not null)
        {
            return _fixedKeyBytes;
        }

        // The outer authentication handler removes any caller-supplied service
        // header and adds one trusted credential snapshot before this handler runs.
        return request.Headers.TryGetValues(DashboardServiceCredentialHandler.ServiceHeader, out var values)
            ? Encoding.UTF8.GetBytes(values.SingleOrDefault() ?? string.Empty)
            : [];
    }

    private static string Sign(byte[] keyBytes, string canonical)
    {
        var digest = HMACSHA256.HashData(keyBytes, Encoding.UTF8.GetBytes(canonical));
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
