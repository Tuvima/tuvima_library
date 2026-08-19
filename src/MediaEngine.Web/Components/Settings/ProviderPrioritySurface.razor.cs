using System.Text.Json;
using MediaEngine.Contracts.Settings;
using MediaEngine.Domain.Enums;
using MediaEngine.Web.Components.Shared;
using MediaEngine.Web.Models.ViewDTOs;
using MediaEngine.Web.Services.Integration;
using Microsoft.JSInterop;
using MudBlazor;

namespace MediaEngine.Web.Components.Settings;

public partial class ProviderPrioritySurface
{
    private static readonly string[] MediaTypes = ["Books", "Audiobooks", "Comics", "Movies", "Music", "TV"];
    private static readonly IReadOnlyList<AppSelectOption> StrategyOptions =
    [
        new("Waterfall", "Waterfall"),
        new("Cascade", "Cascade"),
        new("Sequential", "Sequential"),
    ];

    private PipelineConfiguration _configuration = new();
    private PipelineConfiguration _defaultConfiguration = new();
    private IReadOnlyList<ProviderCatalogueDto> _catalogue = [];
    private Dictionary<string, ProviderStatusDto> _statuses = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<ProviderPriorityItem>> _chains = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _strategies = new(StringComparer.OrdinalIgnoreCase);
    private string _activeMediaType = "Movies";
    private string _baseline = string.Empty;
    private bool _loading = true;
    private bool _saving;
    private string? _loadError;
    private bool _addDialogOpen;
    private int _dragIndex = -1;

    private List<ProviderPriorityItem> CurrentChain => _chains.GetValueOrDefault(_activeMediaType) ?? [];
    private string CurrentStrategy => _strategies.GetValueOrDefault(_activeMediaType, "Waterfall");
    private bool HasChanges => SerializeState() != _baseline;
    private IReadOnlyList<ProviderPriorityItem> AvailableProviders => _catalogue
        .Where(IsRetailProvider)
        .Where(provider => SupportsMedia(provider, _activeMediaType))
        .Where(provider => CurrentChain.All(item => !string.Equals(item.Key, provider.Name, StringComparison.OrdinalIgnoreCase)))
        .Select(provider => CreateItem(provider.Name, null))
        .OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
        .ToList();

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
                _loadError = "Provider priority configuration is unavailable from the Engine.";
                return;
            }

            foreach (var mediaType in MediaTypes)
            {
                var pipeline = _configuration.GetPipelineForMediaType(mediaType);
                _strategies[mediaType] = FormatStrategy(pipeline.Strategy);
                _chains[mediaType] = pipeline.Providers.OrderBy(provider => provider.Rank)
                    .Select(provider => CreateItem(provider.Name, provider.Purpose)).ToList();
            }
            _baseline = SerializeState();
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Could not load provider priority surface.");
            _loadError = "Provider priority configuration is unavailable. Start the Engine and retry.";
        }
        finally { _loading = false; }
    }

    private ProviderPriorityItem CreateItem(string key, string? purpose)
    {
        var catalog = _catalogue.FirstOrDefault(provider => string.Equals(provider.Name, key, StringComparison.OrdinalIgnoreCase));
        _statuses.TryGetValue(key, out var status);
        var displayName = catalog?.DisplayName ?? status?.DisplayName ?? CatalogueService.GetDisplayName(key);
        var logo = catalog?.IconPath;
        if (!string.IsNullOrWhiteSpace(logo) && !logo.StartsWith('/')) logo = "/" + logo.TrimStart('/');
        return new ProviderPriorityItem
        {
            Key = key,
            DisplayName = displayName,
            LogoUrl = logo,
            Icon = CatalogueService.GetAccent(key, catalog?.MaterialIcon ?? status?.CustomIconName).Icon,
            AccentColor = catalog?.AccentColor ?? CatalogueService.GetAccentColor(key),
            Purpose = purpose,
            GloballyEnabled = status?.Enabled ?? catalog?.Enabled ?? false,
        };
    }

    private void SetStrategy(string? strategy) => _strategies[_activeMediaType] = strategy ?? "Waterfall";
    private void OpenAddDialog() => _addDialogOpen = true;
    private void Add(ProviderPriorityItem provider)
    {
        CurrentChain.Add(provider);
        _addDialogOpen = false;
    }
    private void Remove(int index)
    {
        if (index >= 0 && index < CurrentChain.Count) CurrentChain.RemoveAt(index);
    }
    private void Move(int index, int delta)
    {
        var target = index + delta;
        if (index < 0 || index >= CurrentChain.Count || target < 0 || target >= CurrentChain.Count) return;
        (CurrentChain[index], CurrentChain[target]) = (CurrentChain[target], CurrentChain[index]);
    }
    private void BeginDrag(int index) => _dragIndex = index;
    private void DropAt(int targetIndex)
    {
        if (_dragIndex < 0 || _dragIndex >= CurrentChain.Count || targetIndex < 0 || targetIndex >= CurrentChain.Count) return;
        var item = CurrentChain[_dragIndex];
        CurrentChain.RemoveAt(_dragIndex);
        if (_dragIndex < targetIndex) targetIndex--;
        CurrentChain.Insert(Math.Clamp(targetIndex, 0, CurrentChain.Count), item);
        _dragIndex = -1;
    }

    private void ResetCurrentMediaType()
    {
        if (!_defaultConfiguration.Pipelines.TryGetValue(_activeMediaType, out var pipeline)) return;
        _strategies[_activeMediaType] = FormatStrategy(pipeline.Strategy);
        _chains[_activeMediaType] = pipeline.Providers.OrderBy(provider => provider.Rank)
            .Select(provider => CreateItem(provider.Name, provider.Purpose)).ToList();
    }

    private async Task SaveAsync()
    {
        _saving = true;
        try
        {
            var clone = JsonSerializer.Deserialize<PipelineConfiguration>(JsonSerializer.Serialize(_configuration)) ?? new();
            foreach (var mediaType in MediaTypes)
            {
                if (!clone.Pipelines.TryGetValue(mediaType, out var pipeline))
                {
                    pipeline = new MediaTypePipeline();
                    clone.Pipelines[mediaType] = pipeline;
                }
                var existing = pipeline.Providers.ToDictionary(provider => provider.Name, StringComparer.OrdinalIgnoreCase);
                var defaults = _defaultConfiguration.GetPipelineForMediaType(mediaType).Providers
                    .ToDictionary(provider => provider.Name, StringComparer.OrdinalIgnoreCase);
                pipeline.Strategy = ParseStrategy(_strategies.GetValueOrDefault(mediaType, "Waterfall"));
                pipeline.Providers = _chains.GetValueOrDefault(mediaType, [])
                    .Select((item, index) => CopyProviderEntry(
                        existing.GetValueOrDefault(item.Key) ?? defaults.GetValueOrDefault(item.Key), item, index + 1))
                    .ToList();
            }

            if (!await ApiClient.SavePipelinesAsync(clone))
            {
                Snackbar.Add("Provider priority order could not be saved.", Severity.Error);
                return;
            }
            _configuration = clone;
            _baseline = SerializeState();
            Snackbar.Add("Provider priority order saved.", Severity.Success);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Could not save provider priority order.");
            Snackbar.Add("Provider priority order could not be saved.", Severity.Error);
        }
        finally { _saving = false; }
    }

    private async Task CancelAsync()
    {
        if (HasChanges && !await JS.InvokeAsync<bool>("confirm", "Discard unsaved provider priority changes?")) return;
        Nav.NavigateTo(SettingsNav.RouteFor(SettingsSection.Providers, "overview"));
    }

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

    private bool IsRetailProvider(ProviderCatalogueDto provider) =>
        ProviderCatalogueService.IsVisibleProvider(provider.Name) && provider.HydrationStages.Contains(1);
    private static bool SupportsMedia(ProviderCatalogueDto provider, string mediaType) =>
        provider.MediaTypes.Contains(mediaType, StringComparer.OrdinalIgnoreCase)
        || provider.MediaTypes.Contains("All", StringComparer.OrdinalIgnoreCase);
    private string PriorityLabel(int index) => index == 0 ? "Primary" : CurrentStrategy == "Waterfall" ? "Fallback" : $"Step {index + 1}";
    private string ProviderRoleDescription(ProviderPriorityItem item, int index)
    {
        if (!string.IsNullOrWhiteSpace(item.Purpose)) return $"{DisplayWords(item.Purpose)} role in this pipeline";
        return index == 0 ? "First provider considered" : StrategyDescription(CurrentStrategy);
    }
    private static string StrategyDescription(string strategy) => strategy switch
    {
        "Cascade" => "Runs providers and merges useful claims.",
        "Sequential" => "Runs each configured step in order.",
        _ => "Uses the next provider when an earlier provider has no acceptable match.",
    };
    private static string FormatStrategy(ProviderStrategy strategy) => strategy switch
    {
        ProviderStrategy.Cascade => "Cascade",
        ProviderStrategy.Sequential => "Sequential",
        _ => "Waterfall",
    };
    private static ProviderStrategy ParseStrategy(string strategy) => strategy switch
    {
        "Cascade" => ProviderStrategy.Cascade,
        "Sequential" => ProviderStrategy.Sequential,
        _ => ProviderStrategy.Waterfall,
    };
    private string SerializeState() => string.Join('|', MediaTypes.Select(mediaType =>
        $"{mediaType}:{_strategies.GetValueOrDefault(mediaType)}:{string.Join(',', _chains.GetValueOrDefault(mediaType, []).Select(item => item.Key))}"));
    private static string DisplayMediaType(string mediaType) => mediaType == "TV" ? "TV Shows" : mediaType;
    private static string DisplayWords(string value) => string.Join(' ', value.Split(['_', '-'], StringSplitOptions.RemoveEmptyEntries).Select(word => char.ToUpperInvariant(word[0]) + word[1..]));
    private static string Initials(string name) => string.Concat(name.Split([' ', '.', '_', '-'], StringSplitOptions.RemoveEmptyEntries).Take(2).Select(word => char.ToUpperInvariant(word[0])));
    private static string MediaTypeIcon(string mediaType) => mediaType switch
    {
        "Books" => Icons.Material.Outlined.MenuBook,
        "Audiobooks" => Icons.Material.Outlined.Headphones,
        "Comics" => Icons.Material.Outlined.AutoStories,
        "Movies" => Icons.Material.Outlined.Movie,
        "Music" => Icons.Material.Outlined.MusicNote,
        "TV" => Icons.Material.Outlined.Tv,
        _ => Icons.Material.Outlined.Category,
    };
}
