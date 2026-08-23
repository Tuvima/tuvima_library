using MediaEngine.Contracts.Settings;
using MediaEngine.Domain.Configuration;
using MediaEngine.Web.Components.Shared;
using MediaEngine.Web.Models.ViewDTOs;
using MediaEngine.Web.Services.Integration;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace MediaEngine.Web.Components.Settings;

public partial class ProviderPriorityTab
{
    [Parameter] public string Subsection { get; set; } = "overview";

    internal static readonly string[] CapabilityFilters =
        ["all", ProviderCapabilityId.Identity, ProviderCapabilityId.Metadata, ProviderCapabilityId.Artwork, ProviderCapabilityId.Lyrics, ProviderCapabilityId.Subtitles, ProviderCapabilityId.Ratings, ProviderCapabilityId.Other];
    private static readonly IReadOnlyDictionary<string, string> LogoFallbacks =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["apple_api"] = "/images/providers/apple_books.svg",
            ["comicvine"] = "/images/providers/comicvine.png",
            ["fanart_tv"] = "/images/providers/fanart_tv.png",
            ["lrclib"] = "/images/providers/lrclib.png",
            ["musicbrainz"] = "/images/providers/musicbrainz.svg",
            ["opensubtitles"] = "/images/providers/opensubtitles.png",
            ["open_library"] = "/images/providers/open_library.png",
            ["tmdb"] = "/images/providers/tmdb.svg",
            ["wikidata_reconciliation"] = "/images/providers/wikidata_reconciliation.svg",
        };

    private readonly List<ProviderManagementItem> _providers = [];
    private ProviderManagementItem? _selectedProvider;
    private ProviderEditDrawer? _editDrawer;
    private bool _loading = true;
    private string? _loadError;
    private string? _search;
    private string _capabilityFilter = "all";

    private bool IsPrioritySurface => string.Equals(Subsection, "priority", StringComparison.OrdinalIgnoreCase);
    private bool IsEnrichmentSurface => string.Equals(Subsection, "enrichment", StringComparison.OrdinalIgnoreCase);
    private bool IsHealthSurface => string.Equals(Subsection, "health", StringComparison.OrdinalIgnoreCase);
    private bool IsProvidersSurface => !IsPrioritySurface && !IsEnrichmentSurface && !IsHealthSurface;
    private int EnabledCount => _providers.Count(provider => provider.Enabled);
    private int HealthyCount => _providers.Count(provider => provider.Health == ProviderManagementHealth.Healthy);
    private int IssueCount => _providers.Count(provider => provider.Enabled && provider.Health is
        ProviderManagementHealth.Degraded or ProviderManagementHealth.AuthenticationRequired or ProviderManagementHealth.Unavailable);
    private IReadOnlyList<ProviderManagementItem> FilteredProviders => _providers
        .Where(MatchesCapabilityFilter).Where(MatchesSearch)
        .OrderBy(provider => provider.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();

    protected override async Task OnInitializedAsync()
    {
        if (IsProvidersSurface)
            await LoadProvidersAsync();
    }

    protected override async Task OnParametersSetAsync()
    {
        if (IsProvidersSurface && _providers.Count == 0 && !_loading)
            await LoadProvidersAsync();
    }

    private async Task LoadProvidersAsync()
    {
        _loading = true;
        _loadError = null;
        try
        {
            var catalogueTask = CatalogueService.GetCatalogueAsync();
            var statusTask = ApiClient.GetProviderStatusAsync();
            var healthTask = ApiClient.GetProviderHealthAsync();
            await Task.WhenAll(catalogueTask, statusTask, healthTask);

            var catalogue = await catalogueTask;
            var statuses = await statusTask;
            var health = await healthTask;
            var catalogueByKey = catalogue.ToDictionary(item => item.Name, StringComparer.OrdinalIgnoreCase);
            var healthByKey = health.ToDictionary(item => item.ProviderId, StringComparer.OrdinalIgnoreCase);

            _providers.Clear();
            foreach (var status in statuses.Where(item => ProviderCatalogueService.IsVisibleProvider(item.Name)))
            {
                catalogueByKey.TryGetValue(status.Name, out var catalog);
                healthByKey.TryGetValue(status.Name, out var healthRecord);
                var displayName = !string.IsNullOrWhiteSpace(catalog?.DisplayName)
                    ? catalog.DisplayName
                    : !string.IsNullOrWhiteSpace(status.DisplayName) ? status.DisplayName : CatalogueService.GetDisplayName(status.Name);
                var mediaTypes = (catalog?.MediaTypes.Count > 0 ? catalog.MediaTypes : status.MediaTypes ?? [])
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                var category = catalog?.Category ?? status.Domain;
                var item = new ProviderManagementItem
                {
                    Key = status.Name,
                    DisplayName = displayName,
                    Category = category,
                    Domain = status.Domain,
                    Description = BuildDescription(category, mediaTypes),
                    LogoUrl = ResolveLogo(status.Name, catalog?.IconPath),
                    Icon = CatalogueService.GetAccent(status.Name, catalog?.MaterialIcon ?? status.CustomIconName).Icon,
                    AccentColor = catalog?.AccentColor ?? CatalogueService.GetAccentColor(status.Name),
                    MediaTypes = mediaTypes,
                    Capabilities = catalog?.Capabilities.Count > 0
                        ? catalog.Capabilities.Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                        : DeriveCapabilities(status),
                    HydrationStages = catalog?.HydrationStages.Count > 0 ? catalog.HydrationStages : status.HydrationStages ?? [],
                    SystemRole = catalog?.SystemRole,
                    RequiredSystemProvider = catalog?.RequiredSystemProvider ?? false,
                    Enabled = status.Enabled,
                    RequiresKey = catalog?.RequiresKey ?? status.RequiresApiKey,
                    HasKey = status.HasApiKey,
                    AuthType = catalog?.AuthType ?? status.ApiKeyDelivery ?? "none",
                    LanguageStrategy = NormalizeLanguageStrategy(status.LanguageStrategy ?? catalog?.LanguageStrategy),
                    TimeoutSeconds = status.TimeoutSeconds,
                    ThrottleMs = status.ThrottleMs,
                    MaxConcurrency = status.MaxConcurrency,
                    Endpoints = status.Endpoints is null ? new(StringComparer.OrdinalIgnoreCase)
                        : new Dictionary<string, string>(status.Endpoints, StringComparer.OrdinalIgnoreCase),
                    ConsecutiveFailures = healthRecord?.ConsecutiveFailures ?? status.ConsecutiveFailures,
                    LastCheckedAt = ParseOffset(healthRecord?.LastCheckAt) ?? ParseOffset(status.LastSuccessAt) ?? ParseOffset(status.LastFailureAt),
                    FailureReason = healthRecord?.LastFailureReason ?? status.LastFailureReason,
                };
                item.Health = ResolveHealth(item, healthRecord?.Status ?? status.HealthStatus, status.IsReachable);
                _providers.Add(item);
            }

            if (_providers.Count == 0)
                _loadError = "Provider state is unavailable from the Engine. No sample providers are shown as live configuration.";
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Could not load metadata provider management state.");
            _loadError = "Provider state is unavailable from the Engine. Start the Engine and retry before making changes.";
        }
        finally { _loading = false; }
    }

    private void OpenPriority() => Nav.NavigateTo(SettingsNav.RouteFor(SettingsSection.Providers, "priority"));
    private void OpenEnrichment() => Nav.NavigateTo(SettingsNav.RouteFor(SettingsSection.Providers, "enrichment"));
    private void OpenEditor(ProviderManagementItem provider) => _selectedProvider = provider;
    private void CloseEditor() => _selectedProvider = null;
    private bool IsSelected(ProviderManagementItem provider) =>
        string.Equals(_selectedProvider?.Key, provider.Key, StringComparison.OrdinalIgnoreCase);

    private async Task ToggleProviderAsync(ProviderManagementItem provider, bool enabled)
    {
        if (provider.Toggling || provider.Enabled == enabled) return;
        var previous = provider.Enabled;
        var previousHealth = provider.Health;
        provider.Toggling = true;
        provider.Enabled = enabled;
        provider.Health = enabled ? ResolveHealth(provider, HealthStatusName(previousHealth), previousHealth != ProviderManagementHealth.Unavailable) : ProviderManagementHealth.Disabled;
        try
        {
            if (!await ApiClient.UpdateProviderAsync(provider.Key, enabled))
            {
                provider.Enabled = previous;
                provider.Health = previousHealth;
                Snackbar.Add($"Could not {(enabled ? "enable" : "disable")} {provider.DisplayName}.", Severity.Error);
                return;
            }
            Snackbar.Add($"{provider.DisplayName} {(enabled ? "enabled" : "disabled")}.", Severity.Success);
        }
        finally { provider.Toggling = false; }
    }

    private Task TestSelectedProviderAsync() => _selectedProvider is null ? Task.CompletedTask : TestProviderAsync(_selectedProvider);

    private async Task TestProviderAsync(ProviderManagementItem provider)
    {
        if (provider.Testing || !provider.Enabled) return;
        provider.Testing = true;
        provider.TestMessage = null;
        try
        {
            var result = await ApiClient.TestProviderAsync(provider.Key);
            provider.LastCheckedAt = DateTimeOffset.Now;
            provider.LastResponseTimeMs = result?.ResponseTimeMs > 0 ? result.ResponseTimeMs : null;
            provider.LastTestSucceeded = result?.Success == true;
            provider.TestMessage = result?.Success == true
                ? $"Connection verified{(provider.LastResponseTimeMs.HasValue ? $" in {provider.LastResponseTimeMs} ms" : string.Empty)}."
                : ShortFailure(result?.Message);
            provider.FailureReason = result?.Success == true ? null : ShortFailure(result?.Message);
            provider.Health = result?.Success == true ? ProviderManagementHealth.Healthy : ProviderManagementHealth.Unavailable;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Could not test provider {Provider}", provider.Key);
            provider.LastTestSucceeded = false;
            provider.TestMessage = provider.FailureReason = "Connection test failed.";
            provider.Health = ProviderManagementHealth.Unavailable;
        }
        finally { provider.Testing = false; }
    }

    private async Task<bool> SaveProviderAsync(ProviderEditorDraft draft)
    {
        var provider = _providers.FirstOrDefault(item => string.Equals(item.Key, draft.Key, StringComparison.OrdinalIgnoreCase));
        if (provider is null) return false;
        var endpointKey = provider.Endpoints.ContainsKey("api") ? "api" : provider.Endpoints.Keys.FirstOrDefault() ?? "api";
        if (!await ApiClient.SaveProviderConfigAsync(provider.Key, draft.ToUpdate(endpointKey)))
        {
            Snackbar.Add($"Could not save {provider.DisplayName} settings.", Severity.Error);
            return false;
        }

        provider.Enabled = draft.Enabled;
        provider.TimeoutSeconds = draft.TimeoutSeconds;
        provider.ThrottleMs = draft.ThrottleMs;
        provider.MaxConcurrency = draft.MaxConcurrency;
        provider.LanguageStrategy = draft.LanguageStrategy;
        if (!string.IsNullOrWhiteSpace(draft.PrimaryEndpoint)) provider.Endpoints[endpointKey] = draft.PrimaryEndpoint.Trim();
        if (!string.IsNullOrWhiteSpace(draft.ApiKeyReplacement)) provider.HasKey = true;
        provider.Health = draft.Enabled ? ResolveHealth(provider, HealthStatusName(provider.Health), provider.Health != ProviderManagementHealth.Unavailable) : ProviderManagementHealth.Disabled;
        Snackbar.Add($"{provider.DisplayName} settings saved. Connection changes apply after the Engine restarts.", Severity.Success);
        return true;
    }

    private bool MatchesCapabilityFilter(ProviderManagementItem provider)
    {
        if (string.Equals(_capabilityFilter, "all", StringComparison.OrdinalIgnoreCase)) return true;
        if (string.Equals(_capabilityFilter, ProviderCapabilityId.Other, StringComparison.OrdinalIgnoreCase))
        {
            var primaryFilters = CapabilityFilters.Where(filter => filter is not ("all" or ProviderCapabilityId.Other));
            return provider.Capabilities.Any(capability => !primaryFilters.Contains(capability, StringComparer.OrdinalIgnoreCase));
        }
        return provider.Capabilities.Contains(_capabilityFilter, StringComparer.OrdinalIgnoreCase);
    }
    private bool MatchesSearch(ProviderManagementItem provider)
    {
        if (string.IsNullOrWhiteSpace(_search)) return true;
        var search = _search.Trim();
        return provider.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase)
               || provider.Key.Contains(search, StringComparison.OrdinalIgnoreCase)
               || provider.Description.Contains(search, StringComparison.OrdinalIgnoreCase)
               || provider.Capabilities.Any(capability => ProviderCapabilityPresentation.Label(capability).Contains(search, StringComparison.OrdinalIgnoreCase))
               || provider.MediaTypes.Any(type => DisplayMediaType(type).Contains(search, StringComparison.OrdinalIgnoreCase));
    }

    private static ProviderManagementHealth ResolveHealth(ProviderManagementItem item, string? status, bool reachable)
    {
        if (!item.Enabled) return ProviderManagementHealth.Disabled;
        if (item.RequiresKey && !item.HasKey) return ProviderManagementHealth.AuthenticationRequired;
        return status?.Trim().ToLowerInvariant() switch
        {
            "healthy" => ProviderManagementHealth.Healthy,
            "degraded" => ProviderManagementHealth.Degraded,
            "down" or "offline" => ProviderManagementHealth.Unavailable,
            _ when reachable => ProviderManagementHealth.Healthy,
            _ => ProviderManagementHealth.NotChecked,
        };
    }
    private static string? HealthStatusName(ProviderManagementHealth health) => health switch
    {
        ProviderManagementHealth.Healthy => "Healthy",
        ProviderManagementHealth.Degraded => "Degraded",
        ProviderManagementHealth.Unavailable => "Down",
        _ => null,
    };
    private static string HealthLabel(ProviderManagementItem item) => item.Health switch
    {
        ProviderManagementHealth.Healthy => "Healthy",
        ProviderManagementHealth.Degraded => "Degraded",
        ProviderManagementHealth.AuthenticationRequired => "Authentication required",
        ProviderManagementHealth.Unavailable => "Unavailable",
        ProviderManagementHealth.Disabled => "Disabled",
        _ => "Not checked",
    };
    private static AppUiTone HealthTone(ProviderManagementItem item) => item.Health switch
    {
        ProviderManagementHealth.Healthy => AppUiTone.Success,
        ProviderManagementHealth.Degraded or ProviderManagementHealth.AuthenticationRequired => AppUiTone.Warning,
        ProviderManagementHealth.Unavailable => AppUiTone.Error,
        _ => AppUiTone.Neutral,
    };
    internal static bool ShouldShowFailure(ProviderManagementItem item) =>
        !string.IsNullOrWhiteSpace(item.FailureReason) && item.Health is
            ProviderManagementHealth.Degraded or ProviderManagementHealth.AuthenticationRequired or ProviderManagementHealth.Unavailable;
    private static string HealthIcon(ProviderManagementItem item) => item.Health switch
    {
        ProviderManagementHealth.Healthy => Icons.Material.Outlined.FavoriteBorder,
        ProviderManagementHealth.Degraded => Icons.Material.Outlined.WarningAmber,
        ProviderManagementHealth.AuthenticationRequired => Icons.Material.Outlined.Key,
        ProviderManagementHealth.Unavailable => Icons.Material.Outlined.CloudOff,
        ProviderManagementHealth.Disabled => Icons.Material.Outlined.PauseCircleOutline,
        _ => Icons.Material.Outlined.HelpOutline,
    };
    private static string LastCheckedLabel(ProviderManagementItem item) => item.LastCheckedAt.HasValue
        ? $"Last checked {FormatRelative(item.LastCheckedAt.Value)}" : "No health check recorded";
    private static string LastCheckedValue(ProviderManagementItem item) => item.LastCheckedAt.HasValue
        ? FormatRelative(item.LastCheckedAt.Value) : "Not tested";
    private static string FormatRelative(DateTimeOffset value)
    {
        var elapsed = DateTimeOffset.Now - value.ToLocalTime();
        if (elapsed.TotalMinutes < 1) return "just now";
        if (elapsed.TotalMinutes < 60) return $"{Math.Max(1, (int)elapsed.TotalMinutes)} min ago";
        if (elapsed.TotalHours < 24) return $"{(int)elapsed.TotalHours} hr ago";
        return value.LocalDateTime.ToString("MMM d, yyyy");
    }
    private static string BuildDescription(string category, IReadOnlyCollection<string> mediaTypes) =>
        $"{category} provider for {(mediaTypes.Count == 0 ? "library metadata" : string.Join(", ", mediaTypes.Select(DisplayMediaType)))}.";
    private static List<string> DeriveCapabilities(ProviderStatusDto status)
    {
        var fields = status.AvailableFields ?? [];
        var capabilities = fields.Select(ProviderCapabilityPresentation.CapabilityForField)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (status.HydrationStages?.Contains(1) == true) capabilities.Insert(0, ProviderCapabilityId.Identity);
        if (capabilities.Count == 0) capabilities.Add(ProviderCapabilityId.Other);
        return capabilities.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
    private static string CapabilityFilterLabel(string capability) =>
        string.Equals(capability, "all", StringComparison.OrdinalIgnoreCase) ? "All" : ProviderCapabilityPresentation.Label(capability);
    private static AppUiTone CapabilityTone(string capability) => capability switch
    {
        ProviderCapabilityId.Identity => AppUiTone.Info,
        ProviderCapabilityId.Artwork or ProviderCapabilityId.Lyrics or ProviderCapabilityId.Subtitles => AppUiTone.Primary,
        ProviderCapabilityId.Ratings => AppUiTone.Warning,
        _ => AppUiTone.Neutral,
    };
    private static string SystemRoleLabel(string role) => role switch
    {
        "canonical_source" => "Canonical source",
        _ => DisplayWords(role),
    };
    private static string? ResolveLogo(string key, string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured)) return configured.StartsWith('/') ? configured : "/" + configured.TrimStart('/');
        return LogoFallbacks.GetValueOrDefault(key);
    }
    private static string Initials(string name) => string.Concat(name.Split([' ', '.', '_', '-'], StringSplitOptions.RemoveEmptyEntries).Take(2).Select(word => char.ToUpperInvariant(word[0])));
    private static string DisplayMediaType(string mediaType) => mediaType switch
    {
        "TV" => "TV Shows",
        "Comic" => "Comics",
        _ => mediaType,
    };
    private static string DisplayWords(string value) => string.Join(' ', value.Split(['_', '-'], StringSplitOptions.RemoveEmptyEntries)
        .Select(word => char.ToUpperInvariant(word[0]) + word[1..]));
    private static string MediaTypeIcon(string mediaType) => mediaType switch
    {
        "Books" => Icons.Material.Outlined.MenuBook,
        "Audiobooks" => Icons.Material.Outlined.Headphones,
        "Comics" or "Comic" => Icons.Material.Outlined.AutoStories,
        "Movies" => Icons.Material.Outlined.Movie,
        "Music" => Icons.Material.Outlined.MusicNote,
        "TV" => Icons.Material.Outlined.Tv,
        _ => Icons.Material.Outlined.Category,
    };
    private static DateTimeOffset? ParseOffset(string? value) => DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;
    private static string NormalizeLanguageStrategy(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "localized" => "localized", "both" => "both", _ => "source",
    };
    private static string ShortFailure(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return "Connection test failed.";
        var normalized = message.Replace("Test failed:", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
        return normalized.Length <= 160 ? normalized : normalized[..157] + "...";
    }
}
