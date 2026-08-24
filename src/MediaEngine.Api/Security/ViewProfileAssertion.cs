using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using MediaEngine.Api.Services.View;

namespace MediaEngine.Api.Security;

/// <summary>
/// Verifies a short-lived profile assertion bound to one HTTP request and the
/// already validated API key. The Web client signs the UTF-8 canonical value:
/// <c>{profile:D}\n{unixSeconds}\n{METHOD}\n{pathBase}{path}{query}</c>.
/// The signature is Base64Url HMAC-SHA256 using the raw API key as the HMAC key.
/// </summary>
public static class ViewProfileAssertion
{
    public const string ProfileHeader = "X-Tuvima-View-Profile";
    public const string TimestampHeader = "X-Tuvima-View-Timestamp";
    public const string SignatureHeader = "X-Tuvima-View-Signature";
    public const int DefaultMaxClockSkewSeconds = 120;

    public static ViewRequestProfile? Verify(
        HttpRequest request,
        string rawApiKey,
        string role,
        DateTimeOffset now,
        int maxClockSkewSeconds = DefaultMaxClockSkewSeconds)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!IsViewRequest(request.Path)
            || string.IsNullOrEmpty(rawApiKey)
            || !TryReadHeaders(request, out var profileId, out var timestamp, out var suppliedSignature))
        {
            return null;
        }

        var boundedSkew = Math.Clamp(maxClockSkewSeconds, 1, 300);
        DateTimeOffset assertedAt;
        try
        {
            assertedAt = DateTimeOffset.FromUnixTimeSeconds(timestamp);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }

        if ((now - assertedAt).Duration() > TimeSpan.FromSeconds(boundedSkew))
        {
            return null;
        }

        var canonical = CreateCanonicalValue(request, profileId, timestamp);
        var expected = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(rawApiKey),
            Encoding.UTF8.GetBytes(canonical));
        return expected.Length == suppliedSignature.Length
            && CryptographicOperations.FixedTimeEquals(expected, suppliedSignature)
                ? new ViewRequestProfile(profileId, role)
                : null;
    }

    public static string CreateCanonicalValue(HttpRequest request, Guid profileId, long unixSeconds)
    {
        ArgumentNullException.ThrowIfNull(request);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{profileId:D}\n{unixSeconds}\n{request.Method.ToUpperInvariant()}\n{request.PathBase}{request.Path}{request.QueryString}");
    }

    private static bool TryReadHeaders(
        HttpRequest request,
        out Guid profileId,
        out long timestamp,
        out byte[] signature)
    {
        profileId = Guid.Empty;
        timestamp = 0;
        signature = [];
        return Guid.TryParse(request.Headers[ProfileHeader].ToString(), out profileId)
            && long.TryParse(
                request.Headers[TimestampHeader].ToString(),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out timestamp)
            && TryDecodeBase64Url(request.Headers[SignatureHeader].ToString(), out signature);
    }

    private static bool TryDecodeBase64Url(string value, out byte[] bytes)
    {
        bytes = [];
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            var normalized = value.Replace('-', '+').Replace('_', '/');
            normalized = normalized.PadRight(normalized.Length + ((4 - normalized.Length % 4) % 4), '=');
            bytes = Convert.FromBase64String(normalized);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool IsViewRequest(PathString path) =>
        path.StartsWithSegments("/view", StringComparison.OrdinalIgnoreCase);
}
