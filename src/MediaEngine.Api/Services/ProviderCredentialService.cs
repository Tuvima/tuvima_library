using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MediaEngine.Contracts.Settings;
using MediaEngine.Domain.Configuration;
using MediaEngine.Domain.Contracts;
using MediaEngine.Providers.Contracts;

namespace MediaEngine.Api.Services;

/// <summary>
/// Owns provider credential validation, non-mutating authentication probes,
/// atomic persistence, rotation, removal, and live adapter refresh.
/// Credential material is never included in result DTOs or log messages.
/// </summary>
public sealed class ProviderCredentialService
{
    private static readonly JsonSerializerOptions SecretJsonOptions = new() { WriteIndented = true };
    private static readonly TimeSpan PatternTimeout = TimeSpan.FromMilliseconds(250);

    private readonly IConfigurationLoader _configuration;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IReadOnlyList<IProviderCredentialConsumer> _consumers;

    public ProviderCredentialService(
        IConfigurationLoader configuration,
        IHttpClientFactory httpClientFactory,
        IEnumerable<IExternalMetadataProvider> metadataProviders,
        IEnumerable<ITextTrackProvider> textTrackProviders)
    {
        _configuration = configuration;
        _httpClientFactory = httpClientFactory;
        _consumers = metadataProviders.Cast<object>()
            .Concat(textTrackProviders)
            .OfType<IProviderCredentialConsumer>()
            .ToList();
    }

    public async Task<ProviderCredentialOperationResultDto> TestAsync(
        string providerName,
        IReadOnlyDictionary<string, string> submittedCredentials,
        CancellationToken ct = default)
    {
        var provider = _configuration.LoadProvider(providerName);
        if (provider is null)
        {
            return Failure("provider_not_found", "The provider is not available.");
        }

        var fields = provider.Onboarding?.Credentials ?? [];
        var effectiveCredentials = BuildEffectiveCredentials(provider, fields, submittedCredentials);
        var fieldErrors = ValidateCredentials(fields, submittedCredentials, effectiveCredentials);
        if (fieldErrors.Count > 0)
        {
            return new ProviderCredentialOperationResultDto
            {
                Status = "invalid_format",
                Message = "One or more credential fields are missing or have an invalid format.",
                FieldErrors = fieldErrors,
            };
        }

        var probe = provider.Onboarding?.AuthenticationProbe;
        if (probe is null)
        {
            return Failure("probe_unavailable", "This provider does not declare an authentication check.");
        }

        if (!provider.Endpoints.TryGetValue("api", out var baseUrl)
            || !Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri)
            || baseUri.Scheme is not ("http" or "https"))
        {
            return Failure("probe_unavailable", "This provider does not have a valid API endpoint.");
        }

        if (!probe.Path.StartsWith("/", StringComparison.Ordinal)
            || Uri.TryCreate(probe.Path, UriKind.Absolute, out _))
        {
            return Failure("probe_unavailable", "This provider has an invalid authentication check path.");
        }

        var requestUriBuilder = new UriBuilder(baseUri)
        {
            Path = $"{baseUri.AbsolutePath.TrimEnd('/')}{probe.Path}",
            Query = string.Empty,
            Fragment = string.Empty,
        };
        var requestUri = requestUriBuilder.Uri;
        using var request = new HttpRequestMessage(new HttpMethod(probe.Method), requestUri);
        ApplyAuthentication(request, provider, effectiveCredentials);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var response = await _httpClientFactory.CreateClient(provider.Name)
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);
            stopwatch.Stop();

            var statusCode = (int)response.StatusCode;
            if (probe.SuccessStatusCodes.Contains(statusCode))
            {
                return new ProviderCredentialOperationResultDto
                {
                    Success = true,
                    Status = "valid",
                    Message = "The provider accepted the credentials.",
                    ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds,
                };
            }

            var classified = ClassifyStatus(response.StatusCode);
            classified.ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds;
            classified.RetryAfterSeconds = ResolveRetryAfterSeconds(response);
            return classified;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Failure(
                "connectivity_failure",
                "The authentication check timed out before the provider responded.",
                (int)stopwatch.ElapsedMilliseconds);
        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode is { } statusCode)
            {
                var classified = ClassifyStatus(statusCode);
                classified.ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds;
                return classified;
            }

            return Failure(
                "connectivity_failure",
                "The Engine could not establish a connection to the provider.",
                (int)stopwatch.ElapsedMilliseconds);
        }
    }

    public async Task<ProviderCredentialOperationResultDto> SaveAsync(
        string providerName,
        IReadOnlyDictionary<string, string> credentials,
        CancellationToken ct = default)
    {
        var result = await TestAsync(providerName, credentials, ct).ConfigureAwait(false);
        if (!result.Success)
        {
            return result;
        }

        var provider = _configuration.LoadProvider(providerName)!;
        var fields = provider.Onboarding!.Credentials;
        var allowedKeys = fields
            .Select(field => field.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var effectiveCredentials = BuildEffectiveCredentials(provider, fields, credentials);
        var normalized = effectiveCredentials
            .Where(pair => allowedKeys.Contains(pair.Key))
            .ToDictionary(
                pair => pair.Key,
                pair => pair.Value.Trim(),
                StringComparer.OrdinalIgnoreCase);

        WriteSecretFile(provider, normalized);
        ApplyRuntimeCredentials(provider.Name, normalized.ToDictionary(
            pair => pair.Key,
            pair => (string?)pair.Value,
            StringComparer.OrdinalIgnoreCase));

        return result.WithMessage("The provider credentials were verified and saved.");
    }

    public ProviderCredentialOperationResultDto Remove(string providerName)
    {
        var provider = _configuration.LoadProvider(providerName);
        if (provider is null)
        {
            return Failure("provider_not_found", "The provider is not available.");
        }

        var path = GetSecretPath(provider);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        ApplyRuntimeCredentials(provider.Name, new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase));
        return new ProviderCredentialOperationResultDto
        {
            Success = true,
            Status = "removed",
            Message = "The provider credentials were removed.",
        };
    }

    private static Dictionary<string, string> BuildEffectiveCredentials(
        ProviderConfiguration provider,
        IReadOnlyList<ProviderCredentialFieldConfiguration> fields,
        IReadOnlyDictionary<string, string> submitted)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in fields)
        {
            if (submitted.TryGetValue(field.Key, out var submittedValue))
            {
                result[field.Key] = submittedValue.Trim();
                continue;
            }

            var stored = field.Key.ToLowerInvariant() switch
            {
                "api_key" => provider.HttpClient?.ApiKey,
                "username" => provider.HttpClient?.Username,
                "password" => provider.HttpClient?.Password,
                _ => null,
            };
            if (!string.IsNullOrWhiteSpace(stored))
            {
                result[field.Key] = stored;
            }
        }

        return result;
    }

    private static Dictionary<string, string> ValidateCredentials(
        IReadOnlyList<ProviderCredentialFieldConfiguration> fields,
        IReadOnlyDictionary<string, string> submitted,
        IReadOnlyDictionary<string, string> effective)
    {
        var errors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var allowedKeys = fields.Select(field => field.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var key in submitted.Keys.Where(key => !allowedKeys.Contains(key)))
        {
            errors[key] = "This credential field is not supported by the provider.";
        }

        foreach (var field in fields)
        {
            effective.TryGetValue(field.Key, out var value);
            if (field.Required && string.IsNullOrWhiteSpace(value))
            {
                errors[field.Key] = "This credential is required.";
                continue;
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (field.MinimumLength.HasValue && value.Length < field.MinimumLength.Value)
            {
                errors[field.Key] = $"Enter at least {field.MinimumLength.Value} characters.";
            }
            else if (field.MaximumLength.HasValue && value.Length > field.MaximumLength.Value)
            {
                errors[field.Key] = $"Enter no more than {field.MaximumLength.Value} characters.";
            }
            else if (!string.IsNullOrWhiteSpace(field.ValidationPattern)
                     && !Regex.IsMatch(value, field.ValidationPattern, RegexOptions.CultureInvariant, PatternTimeout))
            {
                errors[field.Key] = field.FormatHint ?? "The credential format is invalid.";
            }
        }

        return errors;
    }

    private static void ApplyAuthentication(
        HttpRequestMessage request,
        ProviderConfiguration provider,
        IReadOnlyDictionary<string, string> credentials)
    {
        var http = provider.HttpClient;
        if (http is null)
        {
            return;
        }

        credentials.TryGetValue("api_key", out var apiKey);
        switch (http.ApiKeyDelivery?.ToLowerInvariant())
        {
            case "bearer" when !string.IsNullOrWhiteSpace(apiKey):
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                break;
            case "header" when !string.IsNullOrWhiteSpace(apiKey):
                request.Headers.TryAddWithoutValidation(http.ApiKeyParamName ?? "Api-Key", apiKey);
                break;
            case "query" when !string.IsNullOrWhiteSpace(apiKey):
                var builder = new UriBuilder(request.RequestUri!);
                var separator = string.IsNullOrEmpty(builder.Query) ? string.Empty : "&";
                builder.Query = builder.Query.TrimStart('?')
                    + separator
                    + Uri.EscapeDataString(http.ApiKeyParamName ?? "api_key")
                    + "="
                    + Uri.EscapeDataString(apiKey);
                request.RequestUri = builder.Uri;
                break;
            case "basic":
                credentials.TryGetValue("username", out var username);
                credentials.TryGetValue("password", out var password);
                if (!string.IsNullOrWhiteSpace(username) && !string.IsNullOrWhiteSpace(password))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue(
                        "Basic",
                        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}")));
                }
                break;
        }
    }

    private void WriteSecretFile(ProviderConfiguration provider, IReadOnlyDictionary<string, string> credentials)
    {
        var path = GetSecretPath(provider);
        var directory = Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                directory.FullName,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(credentials, SecretJsonOptions));
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(temporaryPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }

            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private string GetSecretPath(ProviderConfiguration provider)
    {
        if (!string.Equals(Path.GetFileName(provider.Name), provider.Name, StringComparison.Ordinal)
            || provider.Name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new InvalidOperationException("The provider has an invalid credential storage name.");
        }

        var directory = Path.GetFullPath(Path.Combine(_configuration.ConfigDirectoryPath, "secrets"));
        var path = Path.GetFullPath(Path.Combine(directory, $"{provider.Name}.json"));
        if (!path.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The provider credential path escaped the secrets directory.");
        }

        return path;
    }

    private void ApplyRuntimeCredentials(string providerName, IReadOnlyDictionary<string, string?> credentials)
    {
        foreach (var consumer in _consumers.Where(consumer =>
                     string.Equals(consumer.Name, providerName, StringComparison.OrdinalIgnoreCase)))
        {
            consumer.ApplyCredentials(credentials);
        }
    }

    private static ProviderCredentialOperationResultDto ClassifyStatus(HttpStatusCode statusCode) => (int)statusCode switch
    {
        401 or 403 =>
            Failure("invalid_credential", "The provider rejected the credentials."),
        429 =>
            Failure("rate_limited", "The provider rate limit was reached. Try again later."),
        451 =>
            Failure("region_restricted", "The provider is not available from this region."),
        >= 500 =>
            Failure("provider_outage", "The provider is currently unavailable."),
        _ => Failure("connectivity_failure", "The provider returned an unexpected response."),
    };

    private static int? ResolveRetryAfterSeconds(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter?.Delta is { } delta)
        {
            return Math.Max(1, (int)Math.Ceiling(delta.TotalSeconds));
        }

        if (retryAfter?.Date is { } date)
        {
            return Math.Max(1, (int)Math.Ceiling((date - DateTimeOffset.UtcNow).TotalSeconds));
        }

        return null;
    }

    private static ProviderCredentialOperationResultDto Failure(
        string status,
        string message,
        int responseTimeMs = 0) => new()
        {
            Status = status,
            Message = message,
            ResponseTimeMs = responseTimeMs,
        };
}

file static class ProviderCredentialResultExtensions
{
    public static ProviderCredentialOperationResultDto WithMessage(
        this ProviderCredentialOperationResultDto result,
        string message)
    {
        result.Message = message;
        return result;
    }
}
