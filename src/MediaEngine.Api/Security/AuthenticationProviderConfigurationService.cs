using System.Text.Json;
using System.Text.Json.Serialization;
using MediaEngine.Domain.Configuration;
using MediaEngine.Domain.Contracts;

namespace MediaEngine.Api.Security;

public sealed class AuthenticationProviderConfigurationService(IConfigurationLoader configuration)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };
    private readonly SemaphoreSlim writeLock = new(1, 1);

    public AuthSettings LoadWithSecrets()
    {
        var auth = configuration.LoadCore().Auth;
        var secrets = LoadSecrets();
        foreach (var provider in auth.ExternalProviders)
        {
            if (secrets.Providers.TryGetValue(provider.Id, out var secret))
            {
                provider.ClientSecret = secret.ClientSecret;
            }
        }
        return auth;
    }

    public async Task UpdateSecretAsync(
        string providerId,
        string? clientSecret,
        bool clear,
        CancellationToken ct)
    {
        if (clientSecret is null && !clear)
        {
            return;
        }

        await writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var document = LoadSecrets();
            if (clear)
            {
                document.Providers.Remove(providerId);
            }
            else
            {
                document.Providers[providerId] = new ProviderSecret { ClientSecret = clientSecret!.Trim() };
            }

            await WriteSecretsAsync(document, ct).ConfigureAwait(false);
        }
        finally
        {
            writeLock.Release();
        }
    }

    public Task RemoveSecretAsync(string providerId, CancellationToken ct) =>
        UpdateSecretAsync(providerId, null, clear: true, ct);

    private AuthProviderSecrets LoadSecrets()
    {
        var path = SecretPath;
        if (!File.Exists(path))
        {
            return new();
        }

        try
        {
            return JsonSerializer.Deserialize<AuthProviderSecrets>(File.ReadAllText(path), JsonOptions) ?? new();
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                "Authentication provider secrets are invalid and were not changed.", ex);
        }
    }

    private async Task WriteSecretsAsync(AuthProviderSecrets document, CancellationToken ct)
    {
        var path = SecretPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(
                temporary,
                JsonSerializer.Serialize(document, JsonOptions),
                ct).ConfigureAwait(false);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private string SecretPath => Path.Combine(
        Path.GetFullPath(configuration.ConfigDirectoryPath),
        ".secrets",
        "auth-providers.json");

    private sealed class AuthProviderSecrets
    {
        [JsonPropertyName("providers")]
        public Dictionary<string, ProviderSecret> Providers { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class ProviderSecret
    {
        [JsonPropertyName("client_secret")]
        public string ClientSecret { get; init; } = string.Empty;
    }
}
