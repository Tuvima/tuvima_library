using System.Text.Json;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using MediaEngine.Contracts.Authentication;
using Microsoft.AspNetCore.DataProtection;

namespace MediaEngine.Web.Services.Integration;

public sealed class DashboardServiceCredentialProvider(
    IDataProtectionProvider protectionProvider,
    DashboardServiceCredentialProviderOptions options,
    ILogger<DashboardServiceCredentialProvider> logger)
{
    private readonly object _gate = new();
    private byte[]? _bundleHash;
    private string? _token;
    private string? _unavailableState;

    public bool TryGetToken([NotNullWhen(true)] out string? token)
    {
        lock (_gate)
        {
            var path = Path.GetFullPath(Path.Combine(options.ConfigDirectory, ".secrets", "dashboard-engine.credential.json"));
            if (!File.Exists(path))
            {
                MarkUnavailable("missing", path, null);
                token = null;
                return false;
            }

            byte[]? bytes = null;
            try
            {
                // Read the small bundle on every request. File timestamps are not a safe
                // rotation signal because an atomic replacement can retain size and time.
                bytes = File.ReadAllBytes(path);
                var hash = SHA256.HashData(bytes);
                if (_token is not null
                    && _bundleHash is not null
                    && CryptographicOperations.FixedTimeEquals(hash, _bundleHash))
                {
                    token = _token;
                    return true;
                }

                var bundle = JsonSerializer.Deserialize<DashboardServiceCredentialBundle>(bytes)
                    ?? throw new InvalidDataException("The credential bundle is empty.");
                if (string.IsNullOrWhiteSpace(bundle.ProtectedToken))
                {
                    throw new InvalidDataException("The protected credential token is empty.");
                }
                var unprotected = protectionProvider
                    .CreateProtector("Tuvima.DashboardEngineCredential.v1")
                    .Unprotect(bundle.ProtectedToken);
                if (string.IsNullOrWhiteSpace(unprotected))
                {
                    throw new InvalidDataException("The credential token is empty.");
                }

                _bundleHash = hash;
                _token = unprotected;
                _unavailableState = null;
                token = unprotected;
                return true;
            }
            catch (Exception exception) when (exception is IOException
                                               or InvalidDataException
                                               or UnauthorizedAccessException
                                               or JsonException
                                               or CryptographicException
                                               or FormatException)
            {
                var fingerprint = bytes is null
                    ? "unreadable"
                    : Convert.ToHexStringLower(SHA256.HashData(bytes));
                var state = $"invalid:{fingerprint}:{exception.GetType().Name}";
                MarkUnavailable(state, path, exception);
                token = null;
                return false;
            }
        }
    }

    private void MarkUnavailable(string state, string path, Exception? exception)
    {
        _bundleHash = null;
        _token = null;
        if (string.Equals(_unavailableState, state, StringComparison.Ordinal))
        {
            return;
        }

        _unavailableState = state;
        logger.LogWarning(
            exception,
            "Dashboard-to-Engine credential is unavailable at {CredentialPath}; Engine requests will remain blocked until the credential is ready",
            path);
    }
}

public sealed record DashboardServiceCredentialProviderOptions(string ConfigDirectory);
