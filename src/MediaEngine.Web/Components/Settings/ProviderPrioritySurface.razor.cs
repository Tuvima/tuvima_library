using System.Text.Json;
using MediaEngine.Contracts.Settings;
using MediaEngine.Domain.Configuration;
using MediaEngine.Web.Components.Shared;
using MediaEngine.Web.Models.ViewDTOs;
using MediaEngine.Web.Services.Integration;
using Microsoft.AspNetCore.Components.Routing;
using MudBlazor;
using PipelineConfiguration = MediaEngine.Contracts.Settings.PipelineConfiguration;
using PipelineProviderEntry = MediaEngine.Contracts.Settings.PipelineProviderEntry;

namespace MediaEngine.Web.Components.Settings;

public partial class ProviderPrioritySurface
{
    internal static readonly string[] MediaTypes = ["Movies", "TV", "Music", "Books", "Audiobooks", "Comics"];
    private IReadOnlyList<MetadataSelectorItem> MediaSelectorItems => MediaTypes
        .Select(mediaType => new MetadataSelectorItem(mediaType, DisplayMediaType(mediaType), MediaTypeIcon(mediaType)))
        .ToList();

    private sealed class CapabilityState
    {
        public required ProviderCapabilityDefinition Definition { get; init; }
        public List<string> FieldKeys { get; init; } = [];
        public List<ProviderPriorityItem> Providers { get; set; } = [];
    }

    private PipelineConfiguration _configuration = new();
    private PipelineConfiguration _defaultConfiguration = new();
    private IReadOnlyList<ProviderCatalogueDto> _catalogue = [];
    private Dictionary<string, ProviderStatusDto> _statuses = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, Dictionary<string, CapabilityState>> _states = new(StringComparer.OrdinalIgnoreCase);
    private string _activeMediaType = "Movies";
    private string? _selectedCapabilityId;
    private List<string> _editorBaseline = [];
    private string _baseline = string.Empty;
    private bool _loading = true;
    private bool _saving;
    private string? _loadError;
    private bool _addDialogOpen;
    private bool _copyDialogOpen;
    private bool _howDialogOpen;
    private string? _copySourceMediaType;
    private int _dragIndex = -1;

    private async Task ConfirmInternalNavigationAsync(LocationChangingContext context)
    {
        if (!HasChanges) return;

        var confirmed = await JS.InvokeAsync<bool>(
            "confirm",
            new object?[] { "Discard unsaved source priority changes?" });
        if (!confirmed) context.PreventNavigation();
    }

    private IReadOnlyList<CapabilityState> CurrentCapabilities => _states.TryGetValue(_activeMediaType, out var states)
        ? states.Values.OrderBy(state => CapabilityOrder(state.Definition.Id)).ToList()
        : [];
    private CapabilityState? CurrentCapability => _selectedCapabilityId is null
        ? null
        : _states.GetValueOrDefault(_activeMediaType)?.GetValueOrDefault(_selectedCapabilityId);
    private List<ProviderPriorityItem> CurrentChain => CurrentCapability?.Providers ?? [];
    private bool HasChanges => !_loading && SerializeState(_states) != _baseline;
    private bool CanResetCurrentCapability => CurrentCapability is not null
        && BuildStates(_defaultConfiguration).GetValueOrDefault(_activeMediaType)?.ContainsKey(CurrentCapability.Definition.Id) == true;
    private IReadOnlyList<AppSelectOption> CopyOptions => MediaTypes
        .Where(mediaType => !string.Equals(mediaType, _activeMediaType, StringComparison.OrdinalIgnoreCase))
        .Select(mediaType => new AppSelectOption(mediaType, DisplayMediaType(mediaType))).ToList();
    private IReadOnlyList<ProviderPriorityItem> AvailableProviders => CurrentCapability is null
        ? []
        : _catalogue
            .Where(provider => ProviderCatalogueService.IsVisibleProvider(provider.Name))
            .Where(provider => SupportsMedia(provider, _activeMediaType))
            .Where(provider => provider.Capabilities.Contains(CurrentCapability.Definition.Id, StringComparer.OrdinalIgnoreCase))
            .Where(provider => CanAddToCapability(provider, CurrentCapability))
            .Where(provider => CurrentChain.All(item => !string.Equals(item.Key, provider.Name, StringComparison.OrdinalIgnoreCase)))
            .Select(provider => CreateItem(provider.Name, null)).OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
    private IReadOnlyList<ProviderPriorityItem> CurrentSupportProviders => CurrentCapability is null ? [] : GetSupportProviders(CurrentCapability);
    private IReadOnlyList<ProviderPriorityItem> GetSupportProviders(CapabilityState state) => _catalogue
            .Where(provider => ProviderCatalogueService.IsVisibleProvider(provider.Name))
            .Where(provider => SupportsMedia(provider, _activeMediaType))
            .Where(provider => provider.Capabilities.Contains(state.Definition.Id, StringComparer.OrdinalIgnoreCase))
            .Where(provider => !CanAddToCapability(provider, state))
            .Where(provider => state.Providers.All(item => !string.Equals(item.Key, provider.Name, StringComparison.OrdinalIgnoreCase)))
            .Select(provider => CreateItem(provider.Name, null)).OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();

    protected override async Task OnInitializedAsync()
    {
        try
        {
            var pipelineTask = ApiClient.GetPipelinesAsync();
            var defaultsTask = ApiClient.GetDefaultPipelinesAsync();
            var catalogueTask = CatalogueService.GetCatalogueAsync();
            var statusTask = ApiClient.GetProviderStatusAsync();
            await Task.WhenAll(pipelineTask, defaultsTask, catalogueTask, statusTask);

            _configuration = await pipelineTask ?? new();
            _defaultConfiguration = await defaultsTask ?? new();
            _catalogue = await catalogueTask;
            _statuses = (await statusTask).ToDictionary(status => status.Name, StringComparer.OrdinalIgnoreCase);
            if (_configuration.Pipelines.Count == 0)
            {
                _loadError = "Source priority configuration is unavailable from the Engine.";
                return;
            }

            _states = BuildStates(_configuration);
            _baseline = SerializeState(_states);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Could not load source priority surface.");
            _loadError = "Source priority configuration is unavailable. Start the Engine and retry.";
        }
        finally { _loading = false; }
    }

    private Dictionary<string, Dictionary<string, CapabilityState>> BuildStates(PipelineConfiguration source)
    {
        var result = new Dictionary<string, Dictionary<string, CapabilityState>>(StringComparer.OrdinalIgnoreCase);
        foreach (var mediaType in MediaTypes)
        {
            var pipeline = source.GetPipelineForMediaType(mediaType);
            var mediaStates = new Dictionary<string, CapabilityState>(StringComparer.OrdinalIgnoreCase)
            {
                [ProviderCapabilityId.Identity] = new()
                {
                    Definition = ProviderCapabilityPresentation.Get(ProviderCapabilityId.Identity),
                    Providers = pipeline.Providers.OrderBy(provider => provider.Rank)
                        .Select(provider => CreateItem(provider.Name, provider.Purpose)).ToList(),
                },
            };

            foreach (var group in pipeline.FieldPriorities.GroupBy(
                         pair => ProviderCapabilityPresentation.CapabilityForField(pair.Key),
                         StringComparer.OrdinalIgnoreCase))
            {
                var keys = OrderedUnion(group.Select(pair => pair.Value));
                mediaStates[group.Key] = new CapabilityState
                {
                    Definition = ProviderCapabilityPresentation.Get(group.Key),
                    FieldKeys = group.Select(pair => pair.Key).OrderBy(key => key, StringComparer.OrdinalIgnoreCase).ToList(),
                    Providers = keys.Select(key => CreateItem(key, null)).ToList(),
                };
            }

            // Surface provider-declared capabilities even when the Engine owns their
            // execution order (for example, lyrics and subtitles). Those capabilities
            // remain visible and informational instead of implying a fake priority list.
            foreach (var capabilityId in _catalogue
                         .Where(provider => SupportsMedia(provider, mediaType))
                         .SelectMany(provider => provider.Capabilities)
                         .Where(capability => !string.Equals(capability, ProviderCapabilityId.Identity, StringComparison.OrdinalIgnoreCase)
                                              && !string.Equals(capability, ProviderCapabilityId.Other, StringComparison.OrdinalIgnoreCase))
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                mediaStates.TryAdd(capabilityId, new CapabilityState
                {
                    Definition = ProviderCapabilityPresentation.Get(capabilityId),
                });
            }
            result[mediaType] = mediaStates;
        }
        return result;
    }

    private ProviderPriorityItem CreateItem(string key, string? purpose)
    {
        var catalog = _catalogue.FirstOrDefault(provider => string.Equals(provider.Name, key, StringComparison.OrdinalIgnoreCase));
        _statuses.TryGetValue(key, out var status);
        var displayName = catalog?.DisplayName ?? status?.DisplayName;
        var isLocalProcessor = string.Equals(key, "local_processor", StringComparison.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(displayName)) displayName = isLocalProcessor ? "Local file metadata" : CatalogueService.GetDisplayName(key);
        var logo = catalog?.IconPath;
        if (!string.IsNullOrWhiteSpace(logo) && !logo.StartsWith('/')) logo = "/" + logo.TrimStart('/');
        var enabled = status?.Enabled ?? catalog?.Enabled ?? isLocalProcessor;
        return new ProviderPriorityItem
        {
            Key = key,
            DisplayName = displayName,
            LogoUrl = logo,
            Icon = isLocalProcessor ? Icons.Material.Outlined.Folder : CatalogueService.GetAccent(key, catalog?.MaterialIcon ?? status?.CustomIconName).Icon,
            AccentColor = catalog?.AccentColor ?? CatalogueService.GetAccentColor(key),
            Purpose = purpose,
            GloballyEnabled = enabled,
            HealthStatus = enabled ? status?.HealthStatus ?? (status?.IsReachable == true ? "Healthy" : "Not checked") : "Disabled",
            SystemRole = isLocalProcessor ? "local_source" : catalog?.SystemRole,
            RequiredSystemProvider = isLocalProcessor || catalog?.RequiredSystemProvider == true,
        };
    }

    private void SelectMediaType(string mediaType)
    {
        _activeMediaType = mediaType;
        _selectedCapabilityId = null;
    }

    private string MediaTypeClass(string mediaType) =>
        $"source-priority-media__item{(string.Equals(mediaType, _activeMediaType, StringComparison.OrdinalIgnoreCase) ? " is-selected" : string.Empty)}";

    private void OpenCapability(string capabilityId)
    {
        _selectedCapabilityId = capabilityId;
        _editorBaseline = CurrentChain.Select(item => item.Key).ToList();
    }

    private void BackToLanding() => _selectedCapabilityId = null;
    private void OpenAddDialog() => _addDialogOpen = true;
    private void Add(ProviderPriorityItem provider) { CurrentChain.Add(provider); _addDialogOpen = false; }
    private void Remove(int index) { if (index >= 0 && index < CurrentChain.Count && !IsLockedInCurrentCapability(CurrentChain[index])) CurrentChain.RemoveAt(index); }
    private void Move(int index, int delta)
    {
        var target = index + delta;
        if (index < 0 || index >= CurrentChain.Count || target < 0 || target >= CurrentChain.Count) return;
        if (IsLockedInCurrentCapability(CurrentChain[index]) || IsLockedInCurrentCapability(CurrentChain[target])) return;
        (CurrentChain[index], CurrentChain[target]) = (CurrentChain[target], CurrentChain[index]);
    }
    private void BeginDrag(int index) { if (!IsLockedInCurrentCapability(CurrentChain[index])) _dragIndex = index; }
    private void EndDrag() => _dragIndex = -1;
    private void DropAt(int targetIndex)
    {
        if (_dragIndex < 0 || _dragIndex >= CurrentChain.Count || targetIndex < 0 || targetIndex >= CurrentChain.Count) return;
        if (IsLockedInCurrentCapability(CurrentChain[_dragIndex]) || IsLockedInCurrentCapability(CurrentChain[targetIndex])) return;
        var item = CurrentChain[_dragIndex];
        CurrentChain.RemoveAt(_dragIndex);
        CurrentChain.Insert(Math.Clamp(targetIndex, 0, CurrentChain.Count), item);
        _dragIndex = -1;
    }

    private void ResetCurrentCapability()
    {
        if (CurrentCapability is null) return;
        var defaults = BuildStates(_defaultConfiguration).GetValueOrDefault(_activeMediaType)?.GetValueOrDefault(CurrentCapability.Definition.Id);
        if (defaults is null) return;
        CurrentCapability.Providers = defaults.Providers.Select(item => CreateItem(item.Key, item.Purpose)).ToList();
    }

    private async Task ResetAllAsync()
    {
        if (!await JS.InvokeAsync<bool>("confirm", new object?[] { "Reset source priority for every media type and capability to shipped defaults?" })) return;
        _states = BuildStates(_defaultConfiguration);
    }

    private void OpenCopyDialog()
    {
        _copySourceMediaType = MediaTypes.FirstOrDefault(mediaType => !string.Equals(mediaType, _activeMediaType, StringComparison.OrdinalIgnoreCase));
        _copyDialogOpen = true;
    }
    private void SetCopySource(string? mediaType) => _copySourceMediaType = mediaType;
    private void CopyFromSelectedMediaType()
    {
        if (string.IsNullOrWhiteSpace(_copySourceMediaType)) return;
        var sourceStates = _states.GetValueOrDefault(_copySourceMediaType);
        var destinationStates = _states.GetValueOrDefault(_activeMediaType);
        if (sourceStates is null || destinationStates is null) return;

        foreach (var destination in destinationStates.Values)
        {
            if (!sourceStates.TryGetValue(destination.Definition.Id, out var source)) continue;
            var copiedKeys = source.Providers
                .Where(item => CanProviderParticipate(item.Key, _activeMediaType, destination))
                .Select(item => item.Key).ToList();
            foreach (var existing in destination.Providers.Select(item => item.Key))
            {
                if (!copiedKeys.Contains(existing, StringComparer.OrdinalIgnoreCase)) copiedKeys.Add(existing);
            }
            destination.Providers = copiedKeys.Select(key => CreateItem(key, null)).ToList();
        }

        _copyDialogOpen = false;
        Snackbar.Add($"Compatible source priority copied from {DisplayMediaType(_copySourceMediaType)}.", Severity.Success);
    }

    private async Task SaveEditorAsync()
    {
        if (await SaveAsync()) _selectedCapabilityId = null;
    }
    private async Task<bool> SaveAsync()
    {
        _saving = true;
        try
        {
            var clone = JsonSerializer.Deserialize<PipelineConfiguration>(JsonSerializer.Serialize(_configuration)) ?? new();
            foreach (var mediaType in MediaTypes)
            {
                if (!clone.Pipelines.TryGetValue(mediaType, out var pipeline)) continue;
                var mediaStates = _states.GetValueOrDefault(mediaType);
                if (mediaStates is null) continue;

                if (mediaStates.TryGetValue(ProviderCapabilityId.Identity, out var identity))
                {
                    var existing = pipeline.Providers.ToDictionary(provider => provider.Name, StringComparer.OrdinalIgnoreCase);
                    var defaults = _defaultConfiguration.GetPipelineForMediaType(mediaType).Providers
                        .ToDictionary(provider => provider.Name, StringComparer.OrdinalIgnoreCase);
                    pipeline.Providers = identity.Providers.Select((item, index) => CopyProviderEntry(
                        existing.GetValueOrDefault(item.Key) ?? defaults.GetValueOrDefault(item.Key), item, index + 1)).ToList();
                }

                foreach (var state in mediaStates.Values.Where(state => !state.Definition.IsIdentity))
                {
                    foreach (var field in state.FieldKeys)
                    {
                        var existingFieldOrder = pipeline.FieldPriorities.GetValueOrDefault(field) ?? [];
                        pipeline.FieldPriorities[field] = state.Providers
                            .Where(item => existingFieldOrder.Contains(item.Key, StringComparer.OrdinalIgnoreCase)
                                           || ProviderSupportsField(item.Key, field))
                            .Select(item => item.Key)
                            .ToList();
                    }
                }
            }

            if (!await ApiClient.SavePipelinesAsync(clone))
            {
                Snackbar.Add("Source priority could not be saved.", Severity.Error);
                return false;
            }
            _configuration = clone;
            _baseline = SerializeState(_states);
            Snackbar.Add("Source priority saved.", Severity.Success);
            return true;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Could not save source priority.");
            Snackbar.Add("Source priority could not be saved.", Severity.Error);
            return false;
        }
        finally { _saving = false; }
    }

    private void CancelEditor()
    {
        if (CurrentCapability is not null) CurrentCapability.Providers = _editorBaseline.Select(key => CreateItem(key, null)).ToList();
        _selectedCapabilityId = null;
    }
    private async Task BackToProvidersAsync()
    {
        if (HasChanges && !await JS.InvokeAsync<bool>("confirm", new object?[] { "Discard unsaved source priority changes?" })) return;
        Nav.NavigateTo(SettingsNav.RouteFor(SettingsSection.Providers, "overview"));
    }

    private bool IsLockedInCurrentCapability(ProviderPriorityItem item) =>
        string.Equals(item.SystemRole, "local_source", StringComparison.OrdinalIgnoreCase)
        || item.RequiredSystemProvider;
    private bool CanAddToCapability(ProviderCatalogueDto provider, CapabilityState state) =>
        state.Definition.IsIdentity
            ? provider.HydrationStages.Contains(1)
            : state.FieldKeys.Count > 0 && provider.HydrationStages.Contains(1);
    private bool CanProviderParticipate(string providerKey, string mediaType, CapabilityState state)
    {
        if (string.Equals(providerKey, "local_processor", StringComparison.OrdinalIgnoreCase)) return true;
        var provider = _catalogue.FirstOrDefault(item => string.Equals(item.Name, providerKey, StringComparison.OrdinalIgnoreCase));
        return provider is not null && SupportsMedia(provider, mediaType)
            && provider.Capabilities.Contains(state.Definition.Id, StringComparer.OrdinalIgnoreCase)
            && (state.Definition.IsIdentity ? provider.HydrationStages.Contains(1) : provider.HydrationStages.Contains(1) || state.Providers.Any(item => string.Equals(item.Key, providerKey, StringComparison.OrdinalIgnoreCase)));
    }
    private bool ProviderSupportsField(string providerKey, string field) =>
        _statuses.TryGetValue(providerKey, out var status)
        && status.AvailableFields?.Contains(field, StringComparer.OrdinalIgnoreCase) == true;
    private int ProviderCount(CapabilityState state) => state.Providers.Count + GetSupportProviders(state).Count;

    private static PipelineProviderEntry CopyProviderEntry(PipelineProviderEntry? source, ProviderPriorityItem item, int rank) => new()
    {
        Rank = rank,
        Name = item.Key,
        Purpose = item.Purpose ?? source?.Purpose,
        RequiresIdentity = source?.RequiresIdentity ?? false,
        UseAsIdentityFallback = source?.UseAsIdentityFallback ?? false,
        AcceptedTransition = source?.AcceptedTransition,
        AcceptedActions = source?.AcceptedActions is null ? [] : [.. source.AcceptedActions],
    };
    private static List<string> OrderedUnion(IEnumerable<List<string>> orders)
    {
        var result = new List<string>();
        foreach (var order in orders)
        foreach (var provider in order)
            if (!result.Contains(provider, StringComparer.OrdinalIgnoreCase)) result.Add(provider);
        return result;
    }
    private static bool SupportsMedia(ProviderCatalogueDto provider, string mediaType) =>
        provider.MediaTypes.Count == 0 || provider.MediaTypes.Any(type => string.Equals(CanonicalMediaType(type), CanonicalMediaType(mediaType), StringComparison.OrdinalIgnoreCase))
        || provider.MediaTypes.Contains("All", StringComparer.OrdinalIgnoreCase);
    private static string CanonicalMediaType(string mediaType) => mediaType switch
    {
        "Comic" => "Comics",
        "TV Shows" => "TV",
        _ => mediaType,
    };
    private string SerializeState(Dictionary<string, Dictionary<string, CapabilityState>> states) => string.Join('|', MediaTypes.SelectMany(mediaType =>
        states.GetValueOrDefault(mediaType)?.Values.OrderBy(state => state.Definition.Id, StringComparer.OrdinalIgnoreCase)
            .Select(state => $"{mediaType}:{state.Definition.Id}:{string.Join(',', state.Providers.Select(item => item.Key))}") ?? []));
    private static int CapabilityOrder(string capabilityId)
    {
        var index = ProviderCapabilityPresentation.All.ToList().FindIndex(item => string.Equals(item.Id, capabilityId, StringComparison.OrdinalIgnoreCase));
        return index < 0 ? int.MaxValue : index;
    }
    private string PriorityLabel(ProviderPriorityItem item, int index)
    {
        if (item.RequiredSystemProvider || string.Equals(item.SystemRole, "local_source", StringComparison.OrdinalIgnoreCase)) return "Support";
        return index == 0 ? "Primary" : "Fallback";
    }
    private AppUiTone PriorityTone(ProviderPriorityItem item, int index) =>
        string.Equals(PriorityLabel(item, index), "Primary", StringComparison.OrdinalIgnoreCase) ? AppUiTone.Primary : AppUiTone.Info;
    private string ProviderRoleDescription(ProviderPriorityItem item, int index)
    {
        if (string.Equals(item.SystemRole, "canonical_source", StringComparison.OrdinalIgnoreCase)) return "Canonical source for supported fields";
        if (string.Equals(item.SystemRole, "local_source", StringComparison.OrdinalIgnoreCase)) return "Local file values are retained when applicable";
        if (!string.IsNullOrWhiteSpace(item.Purpose)) return $"{DisplayWords(item.Purpose)} role in the identity pipeline";
        return index == 0 ? "Used first when available" : "Used when an earlier source has no value";
    }
    private static AppUiTone ProviderHealthTone(ProviderPriorityItem item) => item.HealthStatus.ToLowerInvariant() switch
    {
        "healthy" => AppUiTone.Success,
        "degraded" => AppUiTone.Warning,
        "down" or "unavailable" or "authentication required" => AppUiTone.Error,
        "disabled" => AppUiTone.Neutral,
        _ => AppUiTone.Neutral,
    };
    private static string DisplayMediaType(string mediaType) => mediaType == "TV" ? "TV Shows" : mediaType;
    private static string DisplayWords(string value) => string.Join(' ', value.Split(['_', '-'], StringSplitOptions.RemoveEmptyEntries).Select(word => char.ToUpperInvariant(word[0]) + word[1..]));
    private static string Initials(string name) => string.Concat(name.Split([' ', '.', '_', '-'], StringSplitOptions.RemoveEmptyEntries).Take(2).Select(word => char.ToUpperInvariant(word[0])));
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
}
