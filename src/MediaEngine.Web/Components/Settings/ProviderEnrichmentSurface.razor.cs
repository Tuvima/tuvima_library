using System.Text.Json;
using MediaEngine.Contracts.Settings;
using MediaEngine.Domain.Configuration;
using MediaEngine.Web.Components.Shared;
using MediaEngine.Web.Models.ViewDTOs;
using MediaEngine.Web.Services.Integration;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using PipelineConfiguration = MediaEngine.Contracts.Settings.PipelineConfiguration;

namespace MediaEngine.Web.Components.Settings;

public partial class ProviderEnrichmentSurface
{
    internal enum EnrichmentPolicyKind
    {
        Artwork,
        People,
        Relationships,
        Series,
        ProviderManaged,
    }

    internal sealed record EnrichmentPolicyItem(
        string Id,
        string Title,
        string Description,
        string CapabilityId,
        string Category,
        string Icon,
        EnrichmentPolicyKind PolicyKind,
        bool HasSourcePriority);

    internal static readonly string[] Categories = ["All", "Artwork", "Audio", "Video", "Books", "Relationships", "Other"];
    private static readonly JsonSerializerOptions CloneOptions = new(JsonSerializerDefaults.Web);
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

    private readonly List<EnrichmentPolicyItem> _items = [];
    private IReadOnlyList<ProviderCatalogueDto> _catalogue = [];
    private IReadOnlyDictionary<string, ProviderStatusDto> _statuses =
        new Dictionary<string, ProviderStatusDto>(StringComparer.OrdinalIgnoreCase);
    private HydrationSettingsDto? _hydration;
    private PipelineConfiguration _pipelines = new();
    private HydrationSettingsDto? _draft;
    private EnrichmentPolicyItem? _selectedItem;
    private string _activeCategory = "All";
    private string? _draftBaseline;
    private string? _loadError;
    private bool _loading = true;
    private bool _saving;
    private bool _running;

    private IReadOnlyList<EnrichmentPolicyItem> FilteredItems => _items
        .Where(item => string.Equals(_activeCategory, "All", StringComparison.OrdinalIgnoreCase)
                       || string.Equals(item.Category, _activeCategory, StringComparison.OrdinalIgnoreCase))
        .ToList();
    private IReadOnlyList<MetadataSelectorItem> CategorySelectorItems => Categories
        .Select(category => new MetadataSelectorItem(category, category))
        .ToList();
    private int EnabledCount => _items.Count(IsEnabled);
    private int IssueCount => _items.Count(HasIssue);
    private bool DrawerHasChanges => _draft is not null
        && !string.Equals(_draftBaseline, Fingerprint(_draft), StringComparison.Ordinal);

    private void SelectCategory(string category) => _activeCategory = category;

    protected override async Task OnInitializedAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        _loading = true;
        _loadError = null;
        try
        {
            var catalogueTask = CatalogueService.GetCatalogueAsync();
            var statusTask = ApiClient.GetProviderStatusAsync();
            var hydrationTask = ApiClient.GetHydrationSettingsAsync();
            var pipelinesTask = ApiClient.GetPipelinesAsync();
            await Task.WhenAll(catalogueTask, statusTask, hydrationTask, pipelinesTask);

            _catalogue = (await catalogueTask)
                .Where(provider => ProviderCatalogueService.IsVisibleProvider(provider.Name))
                .ToList();
            _statuses = (await statusTask).ToDictionary(item => item.Name, StringComparer.OrdinalIgnoreCase);
            _hydration = await hydrationTask;
            _pipelines = await pipelinesTask ?? new();
            if (_hydration is null)
            {
                _loadError = "Enrichment policy is unavailable from the Engine. No sample settings are shown.";
                return;
            }

            BuildItems();
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Could not load enrichment management state.");
            _loadError = "Enrichment state is unavailable from the Engine. Start the Engine and retry before making changes.";
        }
        finally
        {
            _loading = false;
        }
    }

    private void BuildItems()
    {
        _items.Clear();
        AddIfSupported(new("artwork", "Artwork", "Posters, backdrops, fanart, logos, and clear art.",
            ProviderCapabilityId.Artwork, "Artwork", Icons.Material.Outlined.Image, EnrichmentPolicyKind.Artwork, true));
        AddIfSupported(new("lyrics", "Lyrics", "Lyrics and synchronized lyrics supplied with music.",
            ProviderCapabilityId.Lyrics, "Audio", Icons.Material.Outlined.Lyrics, EnrichmentPolicyKind.ProviderManaged, true));
        AddIfSupported(new("subtitles", "Subtitles", "Subtitles and closed captions for video.",
            ProviderCapabilityId.Subtitles, "Video", Icons.Material.Outlined.Subtitles, EnrichmentPolicyKind.ProviderManaged, true));
        AddIfSupported(new("people", "People", "Biographies, portraits, and primary contributor credits.",
            ProviderCapabilityId.People, "Other", Icons.Material.Outlined.People, EnrichmentPolicyKind.People, true));
        AddIfSupported(new("relationships", "Relationships", "Canonical identity links, universes, and work relationships.",
            ProviderCapabilityId.Relationships, "Relationships", Icons.Material.Outlined.AccountTree, EnrichmentPolicyKind.Relationships, true));
        AddIfSupported(new("series", "Series & Collections", "Series information, sequences, seasons, and volumes.",
            ProviderCapabilityId.Relationships, "Books", Icons.Material.Outlined.CollectionsBookmark, EnrichmentPolicyKind.Series, true));
        AddIfSupported(new("ratings", "Ratings", "Ratings and vote counts when a provider supplies them.",
            ProviderCapabilityId.Ratings, "Other", Icons.Material.Outlined.StarOutline, EnrichmentPolicyKind.ProviderManaged, true));
    }

    private void AddIfSupported(EnrichmentPolicyItem item)
    {
        if (SourcesFor(item).Count > 0)
            _items.Add(item);
    }

    private IReadOnlyList<ProviderCatalogueDto> SourcesFor(EnrichmentPolicyItem item)
    {
        var configuredKeys = _pipelines.Pipelines.Values
            .SelectMany(pipeline => pipeline.FieldPriorities
                .Where(pair => string.Equals(
                    ProviderCapabilityPresentation.CapabilityForField(pair.Key),
                    item.CapabilityId,
                    StringComparison.OrdinalIgnoreCase))
                .SelectMany(pair => pair.Value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return _catalogue
        .Where(provider => provider.Capabilities.Contains(item.CapabilityId, StringComparer.OrdinalIgnoreCase))
        .Where(provider => configuredKeys.Contains(provider.Name)
                           || !provider.HydrationStages.Contains(1)
                           || item.PolicyKind == EnrichmentPolicyKind.ProviderManaged)
        .OrderBy(provider => provider.DisplayName, StringComparer.OrdinalIgnoreCase)
        .ToList();
    }

    private int SourceCount(EnrichmentPolicyItem item) => SourcesFor(item).Count;
    private IReadOnlyList<string> SourceMediaTypes(EnrichmentPolicyItem item) => SourcesFor(item)
        .SelectMany(provider => provider.MediaTypes)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(DisplayMediaType, StringComparer.OrdinalIgnoreCase)
        .ToList();

    private bool IsEnabled(EnrichmentPolicyItem item)
    {
        var hasEnabledSource = SourcesFor(item).Any(SourceIsEnabled);
        return item.PolicyKind == EnrichmentPolicyKind.ProviderManaged
            ? hasEnabledSource
            : hasEnabledSource && _hydration?.Stage3Enabled == true;
    }

    private bool HasIssue(EnrichmentPolicyItem item) => SourcesFor(item)
        .Where(SourceIsEnabled)
        .Any(provider => _statuses.TryGetValue(provider.Name, out var status)
                         && ((status.RequiresApiKey && !status.HasApiKey)
                             || status.HealthStatus is "Down" or "Degraded"));

    private string StatusLabel(EnrichmentPolicyItem item) => HasIssue(item)
        ? "Needs attention"
        : IsEnabled(item) ? "Enabled" : "Disabled";

    private AppUiTone StatusTone(EnrichmentPolicyItem item) => HasIssue(item)
        ? AppUiTone.Warning
        : IsEnabled(item) ? AppUiTone.Success : AppUiTone.Neutral;

    private bool SourceIsEnabled(ProviderCatalogueDto provider) =>
        _statuses.TryGetValue(provider.Name, out var status) ? status.Enabled : provider.Enabled;

    private void OpenPolicy(EnrichmentPolicyItem item)
    {
        _selectedItem = item;
        _draft = Clone(_hydration!);
        _draftBaseline = Fingerprint(_draft);
    }

    private string CategoryClass(string category) =>
        $"enrichment-category{(string.Equals(_activeCategory, category, StringComparison.OrdinalIgnoreCase) ? " is-selected" : string.Empty)}";

    private async Task OnDrawerOpenChangedAsync(bool open)
    {
        if (!open)
            await CloseDrawerAsync();
    }

    private async Task CloseDrawerAsync()
    {
        if (DrawerHasChanges && !await JS.InvokeAsync<bool>("confirm", new object?[] { "Discard unsaved enrichment changes?" }))
            return;

        _selectedItem = null;
        _draft = null;
        _draftBaseline = null;
    }

    private async Task SavePolicyAsync()
    {
        if (_draft is null || _saving)
            return;

        _saving = true;
        try
        {
            if (!await ApiClient.UpdateHydrationSettingsAsync(_draft))
            {
                Snackbar.Add("Could not save enrichment policy.", Severity.Error);
                return;
            }

            _hydration = Clone(_draft);
            _draftBaseline = Fingerprint(_draft);
            Snackbar.Add($"{_selectedItem?.Title ?? "Enrichment"} policy saved.", Severity.Success);
            _selectedItem = null;
            _draft = null;
            _draftBaseline = null;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Could not save enrichment policy.");
            Snackbar.Add("Could not save enrichment policy.", Severity.Error);
        }
        finally
        {
            _saving = false;
        }
    }

    private async Task RunEnrichmentNowAsync()
    {
        if (_running)
            return;

        _running = true;
        try
        {
            await ApiClient.TriggerUniverseEnrichmentAsync();
            Snackbar.Add("Enrichment was queued for eligible library items.", Severity.Success);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Could not trigger enrichment.");
            Snackbar.Add("Could not queue enrichment.", Severity.Error);
        }
        finally
        {
            _running = false;
        }
    }

    private void OpenProviders() => Nav.NavigateTo(SettingsNav.RouteFor(SettingsSection.Providers, "overview"));
    private void OpenPriority() => Nav.NavigateTo(SettingsNav.RouteFor(SettingsSection.Providers, "priority"));

    private string? ProviderLogo(ProviderCatalogueDto provider)
    {
        if (!string.IsNullOrWhiteSpace(provider.IconPath))
            return provider.IconPath.StartsWith('/') ? provider.IconPath : "/" + provider.IconPath.TrimStart('/');
        return LogoFallbacks.GetValueOrDefault(provider.Name);
    }

    private string ProviderIcon(ProviderCatalogueDto provider) => CatalogueService.GetAccent(provider.Name, provider.MaterialIcon).Icon;
    private static string Initials(string name) => string.Concat(name.Split([' ', '.', '_', '-'], StringSplitOptions.RemoveEmptyEntries)
        .Take(2).Select(word => char.ToUpperInvariant(word[0])));
    private static string DisplayMediaType(string mediaType) => mediaType switch
    {
        "TV" => "TV Shows",
        "Comic" => "Comics",
        _ => mediaType,
    };
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

    private static HydrationSettingsDto Clone(HydrationSettingsDto source) =>
        JsonSerializer.Deserialize<HydrationSettingsDto>(JsonSerializer.Serialize(source, CloneOptions), CloneOptions)!;
    private static string Fingerprint(HydrationSettingsDto source) => JsonSerializer.Serialize(source, CloneOptions);
}
