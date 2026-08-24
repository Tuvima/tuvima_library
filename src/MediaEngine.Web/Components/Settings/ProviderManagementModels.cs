using MediaEngine.Contracts.Settings;

namespace MediaEngine.Web.Components.Settings;

public enum ProviderManagementHealth
{
    NotChecked,
    Healthy,
    Degraded,
    AuthenticationRequired,
    Unavailable,
    Disabled,
}

public sealed class ProviderManagementItem
{
    public string Key { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string Domain { get; init; } = string.Empty;
    public string? LogoUrl { get; init; }
    public string Icon { get; init; } = string.Empty;
    public string AccentColor { get; init; } = string.Empty;
    public IReadOnlyList<string> MediaTypes { get; init; } = [];
    public IReadOnlyList<string> Capabilities { get; init; } = [];
    public IReadOnlyList<int> HydrationStages { get; init; } = [];
    public string? SystemRole { get; init; }
    public bool RequiredSystemProvider { get; init; }
    public bool Enabled { get; set; }
    public bool RequiresKey { get; init; }
    public bool HasKey { get; set; }
    public string AuthType { get; init; } = "none";
    public string LanguageStrategy { get; set; } = "source";
    public int TimeoutSeconds { get; set; } = 10;
    public int ThrottleMs { get; set; }
    public int MaxConcurrency { get; set; } = 1;
    public Dictionary<string, string> Endpoints { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public ProviderManagementHealth Health { get; set; }
    public int ConsecutiveFailures { get; set; }
    public DateTimeOffset? LastCheckedAt { get; set; }
    public string? FailureReason { get; set; }
    public int? LastResponseTimeMs { get; set; }
    public bool Testing { get; set; }
    public bool Toggling { get; set; }
    public string? TestMessage { get; set; }
    public bool? LastTestSucceeded { get; set; }

    public string PrimaryEndpoint =>
        Endpoints.TryGetValue("api", out var api) && !string.IsNullOrWhiteSpace(api)
            ? api
            : Endpoints.Values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
}

public sealed class ProviderEditorDraft
{
    public string Key { get; init; } = string.Empty;
    public bool Enabled { get; set; }
    public string ApiKeyReplacement { get; set; } = string.Empty;
    public string LanguageStrategy { get; set; } = "source";
    public int TimeoutSeconds { get; set; } = 10;
    public int ThrottleMs { get; set; }
    public int MaxConcurrency { get; set; } = 1;
    public string PrimaryEndpoint { get; set; } = string.Empty;

    public static ProviderEditorDraft From(ProviderManagementItem item) => new()
    {
        Key = item.Key,
        Enabled = item.Enabled,
        LanguageStrategy = item.LanguageStrategy,
        TimeoutSeconds = item.TimeoutSeconds,
        ThrottleMs = item.ThrottleMs,
        MaxConcurrency = item.MaxConcurrency,
        PrimaryEndpoint = item.PrimaryEndpoint,
    };

    public ProviderConfigUpdateDto ToUpdate(string endpointKey) => new()
    {
        Enabled = Enabled,
        TimeoutSeconds = TimeoutSeconds,
        ThrottleMs = ThrottleMs,
        MaxConcurrency = MaxConcurrency,
        LanguageStrategy = LanguageStrategy,
        Endpoints = string.IsNullOrWhiteSpace(PrimaryEndpoint)
            ? null
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [endpointKey] = PrimaryEndpoint.Trim(),
            },
        ApiKey = string.IsNullOrWhiteSpace(ApiKeyReplacement) ? null : ApiKeyReplacement,
    };

    public string Fingerprint() => string.Join('|',
        Enabled,
        LanguageStrategy,
        TimeoutSeconds,
        ThrottleMs,
        MaxConcurrency,
        PrimaryEndpoint.Trim(),
        ApiKeyReplacement);
}
