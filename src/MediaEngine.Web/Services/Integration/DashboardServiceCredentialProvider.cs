using System.Text.Json;
using MediaEngine.Contracts.Authentication;
using Microsoft.AspNetCore.DataProtection;

namespace MediaEngine.Web.Services.Integration;

public sealed class DashboardServiceCredentialProvider(
    IDataProtectionProvider protectionProvider,
    DashboardServiceCredentialProviderOptions options)
{
    private readonly object _gate = new();
    private string? _token;

    public string GetToken()
    {
        if (_token is not null) return _token;
        lock (_gate)
        {
            if (_token is not null) return _token;
            var path = Path.GetFullPath(Path.Combine(options.ConfigDirectory, ".secrets", "dashboard-engine.credential.json"));
            if (!File.Exists(path))
                throw new InvalidOperationException(
                    $"Dashboard-to-Engine credential '{path}' is missing. Start the Engine first and share the config/.keys volume with the Dashboard.");
            var bundle = JsonSerializer.Deserialize<DashboardServiceCredentialBundle>(File.ReadAllText(path))
                ?? throw new InvalidOperationException("The Dashboard-to-Engine credential bundle is invalid.");
            _token = protectionProvider
                .CreateProtector("Tuvima.DashboardEngineCredential.v1")
                .Unprotect(bundle.ProtectedToken);
            return _token;
        }
    }

}

public sealed record DashboardServiceCredentialProviderOptions(string ConfigDirectory);
