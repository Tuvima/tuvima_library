using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using MediaEngine.Contracts.Settings;
using MediaEngine.Domain.Enums;
using MediaEngine.Web.Components.Shared;
using MediaEngine.Web.Models.ViewDTOs;
using MediaEngine.Web.Services.Integration;
using MudBlazor;

namespace MediaEngine.Web.Components.Settings;

public partial class ProviderPriorityTab
{

    private sealed record ProviderInfo(
        string   Name,
        string   Type,
        bool     RequiresKey,
        string[] Supports,
        int      LatencyMs,
        double   SuccessRate,
        string   AuthType = "api_key");

    private sealed record ProviderInsightCard(
        string Icon,
        string Title,
        string Description,
        IReadOnlyList<string> Chips);

    private sealed record ProviderMetric(string Label, string Value);

    private const string ProviderStageRetail = "retail";
    private const string ProviderStageCanonical = "canonical";
    private const string ProviderStageEnrichment = "enrichment";
    private const string AllProviderFilter = "All";

    private static readonly ProviderStageDef[] ProviderStages =
    [
        new(ProviderStageRetail, 1, "Retail Lookup", "Find metadata from retail sources", "Discover and manage retail data providers."),
        new(ProviderStageCanonical, 2, "Canonical Identity", "Build people and title identity", "Review Wikidata identity and relationship configuration."),
        new(ProviderStageEnrichment, 3, "Enrichment & Artwork", "Add artwork and enrich data", "Manage artwork, lyrics, and subtitle enrichment providers."),
    ];

    private static readonly string[] RetailProviderKeys =
        ["apple_api", "comicvine", "musicbrainz", "open_library", "tmdb"];

    private static readonly string[] CanonicalProviderKeys =
        ["wikidata", "wikidata_reconciliation"];

    private static readonly string[] EnrichmentProviderKeys =
        ["fanart_tv", "lrclib", "opensubtitles"];

    private static readonly IReadOnlyList<AppSelectOption> StrategyOptions =
    [
        new("Waterfall", "Waterfall"),
        new("Cascade", "Cascade"),
        new("Sequential", "Sequential"),
    ];

    private static readonly IReadOnlyList<AppSelectOption> LanguageStrategyOptions =
    [
        new("source", "Source defaults"),
        new("localized", "Localized metadata"),
        new("both", "Source + localized"),
    ];

    private static readonly ProviderInfo[] AllProviderCatalogue =
    [
        new("Apple API",      "Retail", false, ["Books", "Audiobooks", "Music"],                 120, 0.94),
        new("Open Library",   "Open",   false, ["Books"],                                        240, 0.87),
        new("TMDB",           "Retail", true,  ["Movies", "TV"],                                 110, 0.96),
        new("MusicBrainz",    "Open",   false, ["Music"],                                        210, 0.89),
        new("Comic Vine",      "Retail", true,  ["Comics"],                                       200, 0.88),
        new("Wikidata",       "Open",   false, ["Books","Movies","TV","Music","Comics","Audiobooks"], 300, 0.85),
        new("Fanart.tv",      "Image",  true,  ["Movies","TV","Music"],                           180, 0.91),
        new("LRCLIB",         "Enrichment", false, ["Music"],                                     160, 0.90),
        new("OpenSubtitles",   "Enrichment", true, ["Movies", "TV"],                              220, 0.90),
    ];

    private sealed class ProviderAssignment
    {
        public string Name             { get; set; } = "";
        public int    TimeoutMs        { get; set; } = 3000;
        public string ApiKey           { get; set; } = "";
        public string Username         { get; set; } = "";
        public string Password         { get; set; } = "";
        public bool   Enabled          { get; set; } = true;
        public int    Retries          { get; set; } = 1;
        public string LanguageStrategy { get; set; } = "source";
        public string Endpoint         { get; set; } = "";
        public int    ThrottleMs       { get; set; } = 500;
        public int    MaxConcurrency   { get; set; } = 1;
        public string? Purpose          { get; set; }
    }

    private sealed record ProviderTestSessionResult(bool Success, DateTimeOffset TestedAt, string Message);

    private static readonly Dictionary<string, string> DefaultStrategies = new()
    {
        ["Books"]      = "Cascade",
        ["Audiobooks"] = "Sequential",
        ["Movies"]     = "Waterfall",
        ["TV"]         = "Waterfall",
        ["Music"]      = "Sequential",
        ["Comics"]     = "Waterfall",
    };

    private static readonly Dictionary<string, List<ProviderAssignment>> DefaultAssignments = new()
    {
        ["Books"]      = [new() { Name = "Apple API", LanguageStrategy = "localized" }],
        ["Audiobooks"] = [new() { Name = "Apple API", LanguageStrategy = "localized" }],
        ["Movies"]     = [new() { Name = "TMDB", LanguageStrategy = "localized" }],
        ["TV"]         = [new() { Name = "TMDB", LanguageStrategy = "localized" }],
        ["Music"]      =
        [
            new() { Name = "MusicBrainz", LanguageStrategy = "source", Purpose = "identity" },
            new() { Name = "Apple API", LanguageStrategy = "localized", Purpose = "enrichment" },
        ],
        ["Comics"]     = [new() { Name = "Comic Vine", LanguageStrategy = "source" }],
    };

    private static readonly Dictionary<string, string[]> ProviderMediaTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Apple API"]      = ["Books", "Audiobooks", "Music"],
        ["Open Library"]   = ["Books"],
        ["TMDB"]           = ["Movies", "TV"],
        ["MusicBrainz"]    = ["Music"],
        ["Comic Vine"]     = ["Comics"],
        ["Wikidata"]       = ["All"],
        ["Fanart.tv"]      = ["Movies", "TV", "Music"],
        ["LRCLIB"]         = ["Music"],
        ["OpenSubtitles"]  = ["Movies", "TV"],
    };

    private readonly string[] _mediaTypes =
        ["Movies", "TV", "Music", "Books", "Audiobooks", "Comics"];

    private static readonly Dictionary<string, string> ProviderIconFallbacks = new(StringComparer.OrdinalIgnoreCase)
    {
        ["apple_api"] = "images/providers/apple_books.svg",
        ["comicvine"] = "images/providers/comicvine.png",
        ["fanart_tv"] = "images/providers/fanart_tv.png",
        ["lrclib"] = "images/providers/lrclib.png",
        ["musicbrainz"] = "images/providers/musicbrainz.svg",
        ["opensubtitles"] = "images/providers/opensubtitles.png",
        ["open_library"] = "images/providers/open_library.png",
        ["tmdb"] = "images/providers/tmdb.svg",
        ["wikidata"] = "images/providers/wikidata_reconciliation.svg",
        ["wikidata_reconciliation"] = "images/providers/wikidata_reconciliation.svg",
    };

    private string _activeStage = ProviderStageRetail;
    private string _activeTab = "Movies";
    private string? _providerSearch;
    private string _providerMediaFilter = AllProviderFilter;
    private Dictionary<string, List<ProviderAssignment>> _assignments = new();
    private Dictionary<string, string> _strategies = new();
    private PipelineConfiguration _pipelineConfiguration = new();
    private string             _drawerMediaType   = "";
    private int                _drawerIndex       = -1;
    private ProviderAssignment? _drawerAssignment  = null;
    private bool   _addDialogOpen      = false;
    private string _addDialogMediaType = "";
    private string _addDialogSearch    = "";

    private bool _saving = false;
    private bool   _drawerSaving       = false;
    private string _drawerSaveMessage  = "";
    private bool   _drawerSaveSuccess  = false;

    private Dictionary<string, ProviderHealthDto> _healthData = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<ProviderCatalogueDto> _liveCatalogue = [];
    private Dictionary<string, ProviderStatusDto> _providerStatusByKey = new(StringComparer.OrdinalIgnoreCase);
    private HydrationSettingsDto? _hydrationSettings;
    private string? _loadError;
    private bool _engineConfigurationUnavailable;
    private Dictionary<string, List<ProviderAssignment>> _savedAssignments = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string> _savedStrategies = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ProviderTestSessionResult> _lastProviderTests = new(StringComparer.OrdinalIgnoreCase);
    private string? _dragProviderName;
    private string? _dragAssignmentMediaType;
    private int _dragAssignmentIndex = -1;

    private bool HasProviderChanges =>
        !SerializeAssignments(_assignments).Equals(SerializeAssignments(_savedAssignments), StringComparison.Ordinal)
        || !_strategies.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .SequenceEqual(_savedStrategies.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase));

    private ProviderStageDef ActiveStage =>
        ProviderStages.FirstOrDefault(stage => string.Equals(stage.Id, _activeStage, StringComparison.OrdinalIgnoreCase))
        ?? ProviderStages[0];

    private bool IsRetailStage => string.Equals(_activeStage, ProviderStageRetail, StringComparison.OrdinalIgnoreCase);
    private bool IsCanonicalStage => string.Equals(_activeStage, ProviderStageCanonical, StringComparison.OrdinalIgnoreCase);
    private bool IsEnrichmentStage => string.Equals(_activeStage, ProviderStageEnrichment, StringComparison.OrdinalIgnoreCase);
    private bool CanDragProviders => IsRetailStage && !_engineConfigurationUnavailable && _liveCatalogue.Count > 0;

    protected override async Task OnInitializedAsync()
    {
        try
        {
            _liveCatalogue = await CatalogueService.GetCatalogueAsync();
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Could not load live provider catalogue.");
            _liveCatalogue = [];
        }
        await LoadFromEngineAsync();
        await LoadHealthDataAsync();
        await LoadHydrationSettingsAsync();
        SelectFirstAssignmentIfNeeded();
    }

    private async Task LoadHydrationSettingsAsync()
    {
        try
        {
            _hydrationSettings = await ApiClient.GetHydrationSettingsAsync();
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Could not load hydration settings for provider setup tab");
        }
    }

    private async Task LoadFromEngineAsync()
    {
        try
        {
            _providerStatusByKey = BuildProviderStatusLookup(await ApiClient.GetProviderStatusAsync());
            var pipelines = await ApiClient.GetPipelinesAsync();
            if (pipelines?.Pipelines is { Count: > 0 })
            {
                ApplyPipelineConfiguration(pipelines);
                SnapshotProviderBaseline();
                _loadError = null;
                _engineConfigurationUnavailable = false;
                return;
            }

            _loadError = "Provider priority configuration is unavailable from the Engine.";
            _engineConfigurationUnavailable = true;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Could not load provider priority configuration from Engine.");
            _loadError = "Provider priority configuration is unavailable. Start the Engine or retry before editing provider order.";
            _engineConfigurationUnavailable = true;
        }

        InitializeEmptyAssignments();
        SnapshotProviderBaseline();
    }

    private void ApplyPipelineConfiguration(PipelineConfiguration pipelines)
    {
        _pipelineConfiguration = pipelines;
        _assignments = new Dictionary<string, List<ProviderAssignment>>(StringComparer.OrdinalIgnoreCase);
        _strategies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var mediaType in _mediaTypes)
        {
            var pipeline = pipelines.GetPipelineForMediaType(mediaType);
            _assignments[mediaType] = pipeline.Providers
                .OrderBy(provider => provider.Rank)
                .Select(provider =>
                {
                    var assignment = CreateAssignmentFromProviderKey(provider.Name);
                    assignment.Purpose = provider.Purpose;
                    return assignment;
                })
                .ToList();
            _strategies[mediaType] = FormatProviderStrategy(pipeline.Strategy);
        }
    }

    private ProviderAssignment CreateAssignmentFromProviderKey(string providerSlotName)
    {
        var displayName = ResolveDisplayName(providerSlotName);
        var info = AllProviderCatalogue.FirstOrDefault(p =>
            p.Name.Replace(" ", "_").Equals(providerSlotName, StringComparison.OrdinalIgnoreCase));
        var status = FindProviderStatus(providerSlotName) ?? FindProviderStatus(displayName);
        var resolvedName = ResolveProviderDisplayName(status?.Name ?? providerSlotName, status?.DisplayName ?? info?.Name ?? displayName);

        return new ProviderAssignment
        {
            Name             = resolvedName,
            Enabled          = status?.Enabled ?? true,
            TimeoutMs        = (status?.TimeoutSeconds ?? 3) * 1000,
            Endpoint         = ResolvePrimaryEndpoint(status),
            ThrottleMs       = status?.ThrottleMs ?? 500,
            MaxConcurrency   = status?.MaxConcurrency ?? 1,
            LanguageStrategy = NormalizeLanguageStrategy(status?.LanguageStrategy)
                               ?? GetDefaultLanguageStrategy(resolvedName),
            Purpose          = GetDefaultPurpose(resolvedName),
        };
    }

    private static Dictionary<string, ProviderStatusDto> BuildProviderStatusLookup(
        IReadOnlyList<ProviderStatusDto> statuses)
    {
        var lookup = new Dictionary<string, ProviderStatusDto>(StringComparer.OrdinalIgnoreCase);
        foreach (var status in statuses)
        {
            AddProviderStatusLookup(lookup, status.Name, status);
            AddProviderStatusLookup(lookup, status.DisplayName, status);
            AddProviderStatusLookup(lookup, status.Name.Replace("_", " "), status);
            AddProviderStatusLookup(lookup, status.DisplayName.Replace(" ", "_"), status);
            AddProviderStatusLookup(lookup, ResolveProviderDisplayName(status.Name, status.DisplayName), status);
            AddProviderStatusLookup(lookup, NormalizeProviderLookupKey(status.Name), status);
            AddProviderStatusLookup(lookup, NormalizeProviderLookupKey(status.DisplayName), status);
        }

        return lookup;
    }

    private static void AddProviderStatusLookup(
        IDictionary<string, ProviderStatusDto> lookup,
        string? key,
        ProviderStatusDto status)
    {
        if (!string.IsNullOrWhiteSpace(key))
            lookup[key] = status;
    }

    private static string? NormalizeLanguageStrategy(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var candidate = value.Trim().ToLowerInvariant();
        return candidate is "source" or "localized" or "both" ? candidate : null;
    }

    private string GetDefaultLanguageStrategy(string providerName)
    {
        if (_providerStatusByKey.TryGetValue(providerName, out var status))
            return NormalizeLanguageStrategy(status.LanguageStrategy) ?? "source";

        return DefaultAssignments
            .SelectMany(kv => kv.Value)
            .FirstOrDefault(a => string.Equals(a.Name, providerName, StringComparison.OrdinalIgnoreCase))
            ?.LanguageStrategy
            ?? "source";
    }

    private static string? GetDefaultPurpose(string providerName) =>
        DefaultAssignments
            .SelectMany(kv => kv.Value)
            .FirstOrDefault(a => string.Equals(a.Name, providerName, StringComparison.OrdinalIgnoreCase))
            ?.Purpose;

    private ProviderAssignment CreateAssignmentFromProviderName(string providerName)
    {
        var status = FindProviderStatus(providerName);

        return new ProviderAssignment
        {
            Name             = ResolveProviderDisplayName(status?.Name ?? providerName, status?.DisplayName ?? providerName),
            Enabled          = status?.Enabled ?? true,
            TimeoutMs        = (status?.TimeoutSeconds ?? 3) * 1000,
            Endpoint         = ResolvePrimaryEndpoint(status),
            ThrottleMs       = status?.ThrottleMs ?? 500,
            MaxConcurrency   = status?.MaxConcurrency ?? 1,
            LanguageStrategy = NormalizeLanguageStrategy(status?.LanguageStrategy)
                               ?? GetDefaultLanguageStrategy(providerName),
            Purpose          = GetDefaultPurpose(providerName),
        };
    }

    private static string ResolveDisplayName(string slotName)
    {
        var match = AllProviderCatalogue.FirstOrDefault(p =>
            p.Name.Replace(" ", "_").Equals(slotName, StringComparison.OrdinalIgnoreCase));
        if (match is not null) return match.Name;
        return string.Join(' ', slotName.Split('_')
            .Select(w => w.Length > 0 ? char.ToUpperInvariant(w[0]) + w[1..] : w));
    }

    private async Task LoadHealthDataAsync()
    {
        try
        {
            var healthList = await ApiClient.GetProviderHealthAsync();
            foreach (var h in healthList)
            {
                _healthData[h.ProviderId] = h;
            }
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Could not load provider health data for provider priority tab");
        }
    }

    private List<ProviderAssignment> GetChain(string mediaType)
    {
        if (!_assignments.TryGetValue(mediaType, out var list))
        {
            list = [];
            _assignments[mediaType] = list;
        }
        return list;
    }

    private void MoveLeft(string mediaType, int idx)
    {
        if (idx <= 0) return;
        var chain = GetChain(mediaType);
        (chain[idx - 1], chain[idx]) = (chain[idx], chain[idx - 1]);
        if (IsSelectedAssignment(mediaType, idx))
            SelectAssignment(mediaType, idx - 1);
    }

    private void MoveRight(string mediaType, int idx)
    {
        var chain = GetChain(mediaType);
        if (idx >= chain.Count - 1) return;
        (chain[idx], chain[idx + 1]) = (chain[idx + 1], chain[idx]);
        if (IsSelectedAssignment(mediaType, idx))
            SelectAssignment(mediaType, idx + 1);
    }

    private void RemoveFromChain()
    {
        if (_drawerIndex < 0 || string.IsNullOrWhiteSpace(_drawerMediaType))
            return;

        var chain = GetChain(_drawerMediaType);
        if (_drawerIndex >= chain.Count)
            return;

        var removedIndex = _drawerIndex;
        chain.RemoveAt(_drawerIndex);

        _drawerAssignment = null;
        _drawerIndex = -1;

        if (chain.Count > 0)
        {
            SelectAssignment(_drawerMediaType, Math.Min(removedIndex, chain.Count - 1));
            return;
        }

        SelectFirstAssignmentIfNeeded();
    }

    private IReadOnlyList<ProviderInfo> GetProviderCards()
    {
        if (_liveCatalogue.Count == 0)
            return [];

        return GetStageCatalogueEntries(applySearch: true, applyMediaFilter: true)
            .Select(ToProviderInfo)
            .OrderBy(p => GetProviderStageSort(p.Name))
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private IReadOnlyList<ProviderInfo> GetEnrichmentProviderCards() =>
        _liveCatalogue.Count == 0
            ? []
            : GetStageCatalogueEntries(ProviderStageEnrichment, applySearch: false, applyMediaFilter: false)
                .Select(ToProviderInfo)
                .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

    private IEnumerable<ProviderCatalogueDto> GetStageCatalogueEntries(
        bool applySearch,
        bool applyMediaFilter) =>
        GetStageCatalogueEntries(_activeStage, applySearch, applyMediaFilter);

    private IEnumerable<ProviderCatalogueDto> GetStageCatalogueEntries(
        string stageId,
        bool applySearch,
        bool applyMediaFilter)
    {
        var query = _liveCatalogue
            .Where(p => MediaEngine.Web.Models.ProviderAccentMap.IsVisibleProvider(p.Name))
            .Where(p => ProviderBelongsToStage(p, stageId));

        if (applyMediaFilter && !string.Equals(_providerMediaFilter, AllProviderFilter, StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(p =>
                p.MediaTypes.Contains(_providerMediaFilter, StringComparer.OrdinalIgnoreCase)
                || p.MediaTypes.Contains("All", StringComparer.OrdinalIgnoreCase));
        }

        if (applySearch && !string.IsNullOrWhiteSpace(_providerSearch))
        {
            query = query.Where(p =>
                ResolveProviderDisplayName(p.Name, p.DisplayName).Contains(_providerSearch, StringComparison.OrdinalIgnoreCase)
                || p.Name.Contains(_providerSearch, StringComparison.OrdinalIgnoreCase)
                || p.MediaTypes.Any(mt => mt.Contains(_providerSearch, StringComparison.OrdinalIgnoreCase)));
        }

        return query
            .GroupBy(p => ResolveProviderDisplayName(p.Name, p.DisplayName), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First());
    }

    private static ProviderInfo ToProviderInfo(ProviderCatalogueDto p) =>
        new(
            Name: ResolveProviderDisplayName(p.Name, p.DisplayName),
            Type: p.Category,
            RequiresKey: p.RequiresKey,
            Supports: [.. p.MediaTypes],
            LatencyMs: 120,
            SuccessRate: 0.90,
            AuthType: p.AuthType);

    private static int GetProviderStageSort(string providerName)
    {
        var normalized = NormalizeProviderLookupKey(providerName);
        return EnrichmentProviderKeys.Any(key => NormalizeProviderLookupKey(key) == normalized) ? 2
            : CanonicalProviderKeys.Any(key => NormalizeProviderLookupKey(key) == normalized) ? 1
            : 0;
    }

    private bool IsActiveStage(string stageId) =>
        string.Equals(_activeStage, stageId, StringComparison.OrdinalIgnoreCase);

    private void SetProviderStage(string stageId)
    {
        if (IsActiveStage(stageId))
            return;

        _activeStage = stageId;
        _providerMediaFilter = AllProviderFilter;
        _providerSearch = null;
        ClearDragState();
        SelectFirstProviderForActiveStage();
    }

    private void SetProviderMediaFilter(string filter)
    {
        _providerMediaFilter = filter;
        SelectFirstProviderForActiveStage();
    }

    private bool IsActiveProviderFilter(string filter) =>
        string.Equals(_providerMediaFilter, filter, StringComparison.OrdinalIgnoreCase);

    private IReadOnlyList<string> GetProviderFilterOptions()
    {
        var options = GetStageCatalogueEntries(_activeStage, applySearch: false, applyMediaFilter: false)
            .SelectMany(p => p.MediaTypes.Count == 0 ? ["All"] : p.MediaTypes)
            .Where(mt => !string.IsNullOrWhiteSpace(mt))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(mt => mt == "All" ? 0 : 1)
            .ThenBy(GetMediaTypeDisplay, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (!options.Contains(AllProviderFilter, StringComparer.OrdinalIgnoreCase))
            options.Insert(0, AllProviderFilter);

        return options;
    }

    private string GetStageProviderCountLabel()
    {
        var count = GetProviderCards().Count;
        var noun = count == 1 ? "provider" : "providers";
        return $"{count} {noun} available";
    }

    private bool ProviderBelongsToStage(ProviderCatalogueDto provider, string stageId)
    {
        var normalized = NormalizeProviderLookupKey(provider.Name);
        var display = NormalizeProviderLookupKey(provider.DisplayName);
        return stageId switch
        {
            ProviderStageRetail => RetailProviderKeys.Any(key => NormalizeProviderLookupKey(key) == normalized || NormalizeProviderLookupKey(key) == display),
            ProviderStageCanonical => CanonicalProviderKeys.Any(key => NormalizeProviderLookupKey(key) == normalized || NormalizeProviderLookupKey(key) == display),
            ProviderStageEnrichment => EnrichmentProviderKeys.Any(key => NormalizeProviderLookupKey(key) == normalized || NormalizeProviderLookupKey(key) == display),
            _ => false,
        };
    }

    private static string NormalizeProviderLookupKey(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : new string(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static string ResolveProviderDisplayName(string providerKey, string? configuredDisplayName = null)
    {
        var mapped = MediaEngine.Web.Models.ProviderAccentMap.GetDisplayName(providerKey);
        var formatted = MediaEngine.Web.Models.ProviderAccentMap.FormatProviderName(providerKey);
        if (!string.Equals(mapped, formatted, StringComparison.OrdinalIgnoreCase))
            return mapped;

        return string.IsNullOrWhiteSpace(configuredDisplayName)
            ? mapped
            : configuredDisplayName;
    }

    private void SelectFirstAssignmentIfNeeded()
    {
        if (_drawerAssignment is not null)
            return;

        foreach (var mediaType in _mediaTypes)
        {
            var chain = GetChain(mediaType);
            if (chain.Count > 0)
            {
                SelectAssignment(mediaType, 0);
                return;
            }
        }

        var firstProvider = GetProviderCards().FirstOrDefault();
        if (firstProvider is not null)
            SelectProvider(firstProvider.Name);
    }

    private void SelectFirstProviderForActiveStage()
    {
        _drawerAssignment = null;
        _drawerMediaType = "";
        _drawerIndex = -1;
        _drawerSaveMessage = "";
        _drawerSaveSuccess = false;

        if (IsRetailStage)
        {
            foreach (var mediaType in _mediaTypes)
            {
                var chain = GetChain(mediaType);
                if (chain.Count > 0)
                {
                    SelectAssignment(mediaType, 0);
                    return;
                }
            }
        }

        var firstProvider = GetProviderCards().FirstOrDefault();
        if (firstProvider is not null)
            SelectProvider(firstProvider.Name);
    }

    private void SelectProvider(string providerName)
    {
        _drawerMediaType = "";
        _drawerIndex = -1;
        _drawerAssignment = CreateAssignmentFromProviderName(providerName);
        _drawerSaveMessage = "";
        _drawerSaveSuccess = false;
    }

    private void SelectAssignment(string mediaType, int idx)
    {
        var chain = GetChain(mediaType);
        if (idx < 0 || idx >= chain.Count)
            return;

        _activeTab = mediaType;
        _drawerMediaType = mediaType;
        _drawerIndex = idx;
        _drawerAssignment = chain[idx];
        _drawerSaveMessage = "";
        _drawerSaveSuccess = false;
    }

    private bool IsSelectedProvider(string providerName) =>
        string.Equals(_drawerAssignment?.Name, providerName, StringComparison.OrdinalIgnoreCase);

    private bool IsSelectedAssignment(string mediaType, int idx) =>
        idx == _drawerIndex
        && string.Equals(_drawerMediaType, mediaType, StringComparison.OrdinalIgnoreCase);

    private void BeginProviderDrag(string providerName)
    {
        if (!CanDragProviders)
            return;

        _dragProviderName = providerName;
        _dragAssignmentMediaType = null;
        _dragAssignmentIndex = -1;
    }

    private void BeginAssignmentDrag(string mediaType, int idx)
    {
        if (!IsRetailStage)
            return;

        _dragAssignmentMediaType = mediaType;
        _dragAssignmentIndex = idx;
        _dragProviderName = null;
    }

    private void DropProviderOnMediaType(string mediaType)
    {
        if (!IsRetailStage)
            return;

        if (!string.IsNullOrWhiteSpace(_dragProviderName))
        {
            AddProviderToChain(mediaType, _dragProviderName);
            ClearDragState();
            return;
        }

        if (!string.IsNullOrWhiteSpace(_dragAssignmentMediaType) && _dragAssignmentIndex >= 0)
        {
            MoveDraggedAssignment(mediaType, GetChain(mediaType).Count);
            ClearDragState();
        }
    }

    private void DropOnAssignment(string mediaType, int targetIndex)
    {
        if (!IsRetailStage)
            return;

        if (!string.IsNullOrWhiteSpace(_dragProviderName))
        {
            InsertProviderIntoChain(mediaType, targetIndex, _dragProviderName);
            ClearDragState();
            return;
        }

        if (!string.IsNullOrWhiteSpace(_dragAssignmentMediaType) && _dragAssignmentIndex >= 0)
        {
            MoveDraggedAssignment(mediaType, targetIndex);
            ClearDragState();
        }
    }

    private void ClearDragState()
    {
        _dragProviderName = null;
        _dragAssignmentMediaType = null;
        _dragAssignmentIndex = -1;
    }

    private void MoveDraggedAssignment(string targetMediaType, int targetIndex)
    {
        if (string.IsNullOrWhiteSpace(_dragAssignmentMediaType))
            return;

        var sourceChain = GetChain(_dragAssignmentMediaType);
        if (_dragAssignmentIndex < 0 || _dragAssignmentIndex >= sourceChain.Count)
            return;

        var assignment = sourceChain[_dragAssignmentIndex];
        sourceChain.RemoveAt(_dragAssignmentIndex);

        var targetChain = GetChain(targetMediaType);
        if (string.Equals(_dragAssignmentMediaType, targetMediaType, StringComparison.OrdinalIgnoreCase)
            && _dragAssignmentIndex < targetIndex)
        {
            targetIndex--;
        }

        targetIndex = Math.Clamp(targetIndex, 0, targetChain.Count);
        targetChain.Insert(targetIndex, assignment);
        SelectAssignment(targetMediaType, targetIndex);
    }

    private void InsertProviderIntoChain(string mediaType, int targetIndex, string providerName)
    {
        var chain = GetChain(mediaType);
        if (chain.Any(a => string.Equals(a.Name, providerName, StringComparison.OrdinalIgnoreCase)))
            return;

        if (!ProviderSupportsMedia(providerName, mediaType))
            return;

        targetIndex = Math.Clamp(targetIndex, 0, chain.Count);
        chain.Insert(targetIndex, CreateAssignmentFromProviderName(providerName));
        SelectAssignment(mediaType, targetIndex);
    }

    private void AddProviderToChain(string mediaType, string providerName)
    {
        var chain = GetChain(mediaType);
        if (chain.Any(a => string.Equals(a.Name, providerName, StringComparison.OrdinalIgnoreCase)))
            return;

        if (!ProviderSupportsMedia(providerName, mediaType))
            return;

        chain.Add(CreateAssignmentFromProviderName(providerName));
        SelectAssignment(mediaType, chain.Count - 1);
    }

    private async Task TestConnectionAsync()
    {
        if (_drawerAssignment is null) return;
        await SaveDrawerAsync();

        var providerName = ResolveProviderConfigName(_drawerAssignment.Name);
        try
        {
            var result = await ApiClient.TestProviderAsync(providerName);
            if (result is not null)
            {
                _drawerSaveSuccess = result.Success;
                _drawerSaveMessage = result.Success
                    ? $"Connected in {result.ResponseTimeMs}ms - {result.SampleFields?.Count ?? 0} fields returned"
                    : $"Test failed: {result.Message}";
                _lastProviderTests[_drawerAssignment.Name] = new ProviderTestSessionResult(
                    result.Success,
                    DateTimeOffset.Now,
                    result.Success ? _drawerSaveMessage : result.Message);
            }
            else
            {
                _drawerSaveSuccess = false;
                _drawerSaveMessage = "No response from Engine";
            }
        }
        catch (Exception ex)
        {
            _drawerSaveSuccess = false;
            _drawerSaveMessage = $"Error: {ex.Message}";
        }
    }

    private async Task SaveDrawerAsync()
    {
        if (_drawerAssignment is null) return;
        _drawerSaving      = true;
        _drawerSaveMessage = "";
        try
        {
            var providerName = ResolveProviderConfigName(_drawerAssignment.Name);
            var info = GetProviderInfo(_drawerAssignment.Name);
            string? apiKeyValue = null;
            if (info?.RequiresKey == true)
            {
                if (info.AuthType == "basic")
                {
                    apiKeyValue = $"{_drawerAssignment.Username}:{_drawerAssignment.Password}";
                }
                else
                {
                    apiKeyValue = _drawerAssignment.ApiKey;
                }
            }

            var config = new MediaEngine.Web.Models.ViewDTOs.ProviderConfigUpdateDto
            {
                Enabled         = _drawerAssignment.Enabled,
                TimeoutSeconds  = _drawerAssignment.TimeoutMs / 1000,
                ThrottleMs      = _drawerAssignment.ThrottleMs,
                MaxConcurrency  = _drawerAssignment.MaxConcurrency,
                Endpoints       = BuildEndpointUpdate(providerName, _drawerAssignment.Endpoint),
                ApiKey          = apiKeyValue,
                LanguageStrategy = _drawerAssignment.LanguageStrategy,
            };

            var success = await ApiClient.SaveProviderConfigAsync(providerName, config);
            _drawerSaveSuccess = success;
            _drawerSaveMessage = success ? "Saved successfully" : "Failed to save - check Engine connection";
            if (success)
                SnapshotProviderBaseline();
        }
        catch (Exception ex)
        {
            _drawerSaveSuccess = false;
            _drawerSaveMessage = $"Error: {ex.Message}";
        }
        finally
        {
            _drawerSaving = false;
        }
    }

    private void OpenAddDialog(string mediaType)
    {
        _addDialogMediaType = mediaType;
        _addDialogSearch    = "";
        _addDialogOpen      = true;
    }

    private List<ProviderInfo> GetAvailableProviders(string mediaType)
    {
        var chain   = GetChain(mediaType);
        var inChain = chain.Select(a => a.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (_liveCatalogue.Count > 0)
        {
            return _liveCatalogue
                .Where(p => MediaEngine.Web.Models.ProviderAccentMap.IsVisibleProvider(p.Name))
                .Where(p => ProviderBelongsToStage(p, ProviderStageRetail))
                .Select(ToProviderInfo)
                .Where(p => !inChain.Contains(p.Name)
                            && ProviderSupportsMedia(p.Name, mediaType))
                .ToList();
        }
        return [];
    }

    private void AddProviderToChain(string name)
    {
        AddProviderToChain(_addDialogMediaType, name);
        _addDialogOpen = false;
    }

    private void InitializeEmptyAssignments()
    {
        _assignments = _mediaTypes.ToDictionary(
            mediaType => mediaType,
            _ => new List<ProviderAssignment>(),
            StringComparer.OrdinalIgnoreCase);
        _strategies = new Dictionary<string, string>(DefaultStrategies, StringComparer.OrdinalIgnoreCase);
    }

    private string GetStrategy(string mediaType)
    {
        if (_strategies.TryGetValue(mediaType, out var s)) return s;
        return DefaultStrategies.GetValueOrDefault(mediaType, "Waterfall");
    }

    private void SetStrategy(string mediaType, string strategy)
    {
        _strategies[mediaType] = strategy;
    }

    private async Task SaveAsync()
    {
        _saving = true;
        try
        {
            var providerConfigSaved = true;
            var savedProviders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (_, chain) in _assignments)
            {
                foreach (var assignment in chain)
                {
                    if (!savedProviders.Add(assignment.Name)) continue;
                    var providerName = ResolveProviderConfigName(assignment.Name);
                    var info = GetProviderInfo(assignment.Name);
                    string? apiKeyValue = null;
                    if (info?.RequiresKey == true)
                    {
                        apiKeyValue = info.AuthType == "basic"
                            ? $"{assignment.Username}:{assignment.Password}"
                            : assignment.ApiKey;
                    }

                    var config = new MediaEngine.Web.Models.ViewDTOs.ProviderConfigUpdateDto
                    {
                        Enabled        = assignment.Enabled,
                        TimeoutSeconds = assignment.TimeoutMs / 1000,
                        ThrottleMs     = assignment.ThrottleMs,
                        MaxConcurrency = assignment.MaxConcurrency,
                        Endpoints      = BuildEndpointUpdate(providerName, assignment.Endpoint),
                        ApiKey         = apiKeyValue,
                        LanguageStrategy = assignment.LanguageStrategy,
                    };
                    providerConfigSaved &= await ApiClient.SaveProviderConfigAsync(providerName, config);
                }
            }

            if (!providerConfigSaved)
            {
                Snackbar.Add("One or more provider configs could not be saved. Your edits are still visible.", Severity.Error);
                return;
            }

            var pipelines = BuildPipelineConfigurationForSave();
            var pipelinesSaved = await ApiClient.SavePipelinesAsync(pipelines);
            if (!pipelinesSaved)
            {
                Snackbar.Add("Provider priority order could not be saved. Your edits are still visible.", Severity.Error);
                return;
            }
            _pipelineConfiguration = pipelines;
            SnapshotProviderBaseline();
            Snackbar.Add("Provider settings saved to Engine configuration.", Severity.Success);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Could not save provider priority settings.");
            Snackbar.Add("Provider priority settings could not be saved.", Severity.Error);
        }
        finally
        {
            _saving = false;
        }
    }

    private PipelineConfiguration BuildPipelineConfigurationForSave()
    {
        _pipelineConfiguration.Pipelines ??= new Dictionary<string, MediaTypePipeline>(StringComparer.OrdinalIgnoreCase);

        foreach (var mediaType in _mediaTypes)
        {
            if (!_pipelineConfiguration.Pipelines.TryGetValue(mediaType, out var pipeline))
            {
                pipeline = new MediaTypePipeline();
                _pipelineConfiguration.Pipelines[mediaType] = pipeline;
            }

            pipeline.Strategy = ParseProviderStrategy(GetStrategy(mediaType));
            pipeline.Providers = GetChain(mediaType)
                .Select((assignment, index) => new PipelineProviderEntry
                {
                    Rank = index + 1,
                    Name = ResolveProviderConfigName(assignment.Name),
                    Purpose = assignment.Purpose,
                })
                .ToList();
        }

        return _pipelineConfiguration;
    }

    private static string FormatProviderStrategy(ProviderStrategy strategy) => strategy switch
    {
        ProviderStrategy.Cascade => "Cascade",
        ProviderStrategy.Sequential => "Sequential",
        _ => "Waterfall",
    };

    private static ProviderStrategy ParseProviderStrategy(string strategy) =>
        strategy.Trim().ToLowerInvariant() switch
        {
            "cascade" => ProviderStrategy.Cascade,
            "sequential" => ProviderStrategy.Sequential,
            _ => ProviderStrategy.Waterfall,
        };

    private string ResolveProviderConfigName(string providerName)
    {
        var live = FindProviderCatalogueEntry(providerName);
        if (live is not null)
            return live.Name;

        return providerName.Replace(" ", "_").ToLowerInvariant();
    }

    private ProviderCatalogueDto? FindProviderCatalogueEntry(string providerName) =>
        _liveCatalogue.FirstOrDefault(p => ProviderMatches(p, providerName));

    private static bool ProviderMatches(ProviderCatalogueDto provider, string providerName)
    {
        var normalized = NormalizeProviderLookupKey(providerName);
        return NormalizeProviderLookupKey(provider.Name) == normalized
               || NormalizeProviderLookupKey(provider.DisplayName) == normalized
               || NormalizeProviderLookupKey(ResolveProviderDisplayName(provider.Name, provider.DisplayName)) == normalized;
    }

    private ProviderStatusDto? FindProviderStatus(string providerName)
    {
        if (_providerStatusByKey.TryGetValue(providerName, out var status))
            return status;

        if (_providerStatusByKey.TryGetValue(providerName.Replace(" ", "_"), out status))
            return status;

        return _providerStatusByKey.GetValueOrDefault(NormalizeProviderLookupKey(providerName));
    }

    private static string ResolvePrimaryEndpoint(ProviderStatusDto? status)
    {
        if (status?.Endpoints is null || status.Endpoints.Count == 0)
            return string.Empty;

        if (status.Endpoints.TryGetValue("api", out var api) && !string.IsNullOrWhiteSpace(api))
            return api;

        return status.Endpoints.Values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? string.Empty;
    }

    private Dictionary<string, string> BuildEndpointUpdate(string providerName, string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
            return [];

        var status = FindProviderStatus(providerName);
        var key = status?.Endpoints?.ContainsKey("api") == true
            ? "api"
            : status?.Endpoints?.Keys.FirstOrDefault() ?? "api";

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [key] = endpoint,
        };
    }

    private static string GetMediaTypeDisplay(string mediaType) => mediaType switch
    {
        "TV" => "TV Shows",
        "All" => "All media",
        _ => mediaType,
    };

    private string GetProviderAccent(string providerName)
    {
        var live = FindProviderCatalogueEntry(providerName);
        if (!string.IsNullOrWhiteSpace(live?.AccentColor))
            return live.AccentColor;

        var (color, _) = MediaEngine.Web.Models.ProviderAccentMap.GetAccent(ResolveProviderConfigName(providerName));
        return color;
    }

    private string GetProviderDescription(string providerName)
    {
        var info = GetProviderInfo(providerName);
        var mediaTypes = string.Join(", ", GetProviderMediaTypes(providerName).Select(GetMediaTypeDisplay));
        if (string.IsNullOrWhiteSpace(mediaTypes))
            mediaTypes = "metadata";

        return $"{info?.Type ?? "Metadata"} provider for {mediaTypes}.";
    }

    private string GetProviderDomain(string providerName)
    {
        var live = FindProviderCatalogueEntry(providerName);
        if (!string.IsNullOrWhiteSpace(live?.Domain))
            return live.Domain;

        var status = _providerStatusByKey.GetValueOrDefault(providerName)
                     ?? FindProviderStatus(providerName);
        return status?.Domain ?? "Unknown";
    }

    private string? GetProviderLogoUrl(string providerName)
    {
        var live = FindProviderCatalogueEntry(providerName);
        if (!string.IsNullOrWhiteSpace(live?.IconPath))
            return NormalizeAssetPath(live.IconPath);

        var key = ResolveProviderConfigName(providerName);
        return ProviderIconFallbacks.TryGetValue(key, out var path)
            ? NormalizeAssetPath(path)
            : null;
    }

    private static string NormalizeAssetPath(string path)
    {
        var trimmed = path.Trim();
        return trimmed.StartsWith("/", StringComparison.Ordinal)
            ? trimmed
            : "/" + trimmed.TrimStart('/');
    }

    private bool ProviderSupportsMedia(string providerName, string mediaType)
    {
        var types = GetProviderMediaTypes(providerName);
        return types.Contains("All", StringComparer.OrdinalIgnoreCase)
            || types.Contains(mediaType, StringComparer.OrdinalIgnoreCase);
    }

    private static string GetProviderInitials(string providerName)
    {
        var words = providerName
            .Split([' ', '.', '_', '-'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return words.Length == 0
            ? "P"
            : string.Concat(words.Take(2).Select(word => char.ToUpperInvariant(word[0])));
    }

    private static string GetMediaTypeIcon(string mediaType) => mediaType switch
    {
        "Books"      => Icons.Material.Outlined.MenuBook,
        "Audiobooks" => Icons.Material.Outlined.Headphones,
        "Movies"     => Icons.Material.Outlined.Movie,
        "TV"         => Icons.Material.Outlined.Tv,
        "Music"      => Icons.Material.Outlined.MusicNote,
        "Comics"     => Icons.Material.Outlined.AutoStories,
        _            => Icons.Material.Outlined.Category,
    };

    private string GetProviderIcon(string provider)
    {
        var catalogueEntry = FindProviderCatalogueEntry(provider);
        if (catalogueEntry is not null)
        {
            var (_, icon) = MediaEngine.Web.Models.ProviderAccentMap.GetAccent(
                catalogueEntry.Name, catalogueEntry.MaterialIcon);
            return icon;
        }
        return provider switch
        {
            "Apple API"                     => Icons.Material.Outlined.MenuBook,
            "Open Library"                  => Icons.Material.Outlined.LocalLibrary,
            "TMDB"                          => Icons.Material.Outlined.Movie,
            "MusicBrainz"                   => Icons.Material.Outlined.MusicNote,
            "Comic Vine"                    => Icons.Material.Outlined.AutoStories,
            "Wikidata"                      => Icons.Material.Outlined.Collections,
            "Fanart.tv"                     => Icons.Material.Outlined.Image,
            "LRCLIB"                        => Icons.Material.Outlined.Lyrics,
            "OpenSubtitles"                 => Icons.Material.Outlined.Subtitles,
            _                               => Icons.Material.Outlined.Storage,
        };
    }

    private ProviderInfo? GetProviderInfo(string name)
    {
        var live = FindProviderCatalogueEntry(name);
        if (live is not null)
        {
            return new ProviderInfo(
                Name:        ResolveProviderDisplayName(live.Name, live.DisplayName),
                Type:        live.Category,
                RequiresKey: live.RequiresKey,
                Supports:    [.. live.MediaTypes],
                LatencyMs:   120,       // not in catalogue - health data used at runtime
                SuccessRate: 0.90,      // not in catalogue - health data used at runtime
                AuthType:    live.AuthType);
        }
        return AllProviderCatalogue.FirstOrDefault(p => p.Name == name);
    }
    private string GetLatencyDisplay(string providerName)
    {
        var key = ResolveProviderConfigName(providerName);
        if (_healthData.TryGetValue(key, out var h) && h.LastSuccessAt is not null)
            return h.Status;
        var info = GetProviderInfo(providerName);
        return info is not null && _liveCatalogue.Count > 0 ? $"{info.LatencyMs} ms" : "No health yet";
    }
    private string GetSuccessRateDisplay(string providerName)
    {
        var key = ResolveProviderConfigName(providerName);
        if (_healthData.TryGetValue(key, out var h))
        {
            return h.Status switch
            {
                "Healthy"  => "Healthy",
                "Degraded" => $"Degraded ({h.ConsecutiveFailures} failures)",
                "Down"     => $"Down - {h.LastFailureReason ?? "unknown"}",
                _          => h.Status,
            };
        }
        var status = FindProviderStatus(providerName);
        if (status is not null)
            return status.IsReachable ? "Reachable" : "Unavailable";
        return "No status";
    }
    private bool IsProviderHealthy(string providerName)
    {
        var key = ResolveProviderConfigName(providerName);
        if (_healthData.TryGetValue(key, out var h))
            return !string.Equals(h.Status, "Down", StringComparison.OrdinalIgnoreCase);
        var status = FindProviderStatus(providerName);
        return status?.IsReachable == true;
    }

    private string[] GetProviderMediaTypes(string provider)
    {
        var live = FindProviderCatalogueEntry(provider);
        if (live is not null && live.MediaTypes.Count > 0)
            return [.. live.MediaTypes];
        return ProviderMediaTypes.TryGetValue(provider, out var types) ? types : ["All"];
    }

    private string ProviderStatusDescription(ProviderAssignment assignment)
    {
        var status = FindProviderStatus(assignment.Name);
        if (!assignment.Enabled)
            return "Provider is disabled and will be skipped.";
        if (status?.RequiresApiKey == true && !status.HasApiKey)
            return "Provider is enabled but requires credentials before live matching will work.";
        return "Provider is enabled and saved through Engine configuration.";
    }

    private IReadOnlyList<ProviderInsightCard> GetCanonicalSummaryCards()
    {
        var allProperties = WikidataPropertyDefaults.GetAllProperties();
        var bridgeCount = WikidataPropertyDefaults.GetBridgeEntries().Count;
        var universeCount = allProperties.Count(p => p.StageApplicability == "Universe Graph");
        var personCount = allProperties.Count(p => p.EntityScope == "Person");

        return
        [
            new(
                Icons.Material.Outlined.VerifiedUser,
                "Identity Resolution",
                "Wikidata receives bridge IDs from retail providers and resolves the canonical work, creator, title, and release identity.",
                ["95% auto accept", "55% review floor", "15 candidates"]),
            new(
                Icons.Material.Outlined.Link,
                "Bridge Identifiers",
                "Cross-provider IDs connect retail results to stable Wikidata QIDs before canonical data is trusted.",
                [$"{bridgeCount} bridges", "ISBN/ASIN", "TMDB/IMDb", "MusicBrainz"]),
            new(
                Icons.Material.Outlined.Map,
                "Canonical Properties",
                "Core facts, creative credits, series data, narrative context, and external IDs are tracked as claim keys.",
                ["Core facts", "Credits", "Series", "Bridge IDs"]),
            new(
                Icons.Material.Outlined.AccountTree,
                "Universe Relationships",
                "People, characters, locations, organizations, and child entities feed the relationship graph after identity is known.",
                [$"{personCount} person props", $"{universeCount} universe props", "TV/music/comics child discovery"]),
        ];
    }

    private IReadOnlyList<ProviderMetric> GetWikidataMetrics()
    {
        var wikidata = FindProviderStatus("Wikidata") ?? FindProviderStatus("wikidata_reconciliation");
        return
        [
            new("Language Strategy", NormalizeLanguageStrategy(wikidata?.LanguageStrategy) ?? "both"),
            new("Request Throttle", $"{(wikidata?.ThrottleMs ?? 500)}ms"),
            new("Bridge Batch Size", $"{_hydrationSettings?.WikidataBatchSize ?? 50}"),
            new("SPARQL Batch Size", $"{_hydrationSettings?.BatchSparqlSize ?? 50}"),
            new("Lineage Depth", $"{_hydrationSettings?.LineageDepth ?? 2}"),
            new("Fictional Depth", $"{_hydrationSettings?.FictionalEntityEnrichmentDepth ?? 2}"),
            new("Lore Delta", (_hydrationSettings?.LoreDeltaCheckOnExplorerOpen ?? true) ? "On open" : "Manual"),
            new("Stage 3 Refresh", $"{_hydrationSettings?.Stage3RefreshDays ?? 30} days"),
        ];
    }

    private IReadOnlyList<string> GetEnrichmentCapabilityChips(string providerName)
    {
        var live = FindProviderCatalogueEntry(providerName);
        var chips = live?.SearchChips.Values.SelectMany(values => values).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (chips is { Count: > 0 })
            return chips;

        return ResolveProviderConfigName(providerName) switch
        {
            "fanart_tv" => ["Artwork", "Backdrops", "Logos"],
            "lrclib" => ["Synced lyrics", "LRC"],
            "opensubtitles" => ["Subtitles", "WebVTT", "SRT"],
            _ => ["Enrichment"],
        };
    }

    private IReadOnlyList<ProviderMetric> GetEnrichmentMetrics()
    {
        var imageMediaTypes = _hydrationSettings?.Stage3MediaTypesForImages is { Count: > 0 } mediaTypes
            ? string.Join(", ", mediaTypes.Select(GetMediaTypeDisplay))
            : "Movies, TV Shows, Music";

        return
        [
            new("Stage 3", (_hydrationSettings?.Stage3Enabled ?? true) ? "Enabled" : "Disabled"),
            new("Artwork Media", imageMediaTypes),
            new("Fanart Jobs", $"{_hydrationSettings?.MaxConcurrentFanartJobs ?? 1}"),
            new("Sweep Schedule", _hydrationSettings?.Stage3ScheduleCron ?? "0 3 * * *"),
            new("Sweep Size", $"{_hydrationSettings?.Stage3MaxItemsPerSweep ?? 50} items"),
            new("Rate Limit", $"{_hydrationSettings?.Stage3RateLimitMs ?? 2000}ms"),
        ];
    }

    private static void HandleActivationKey(KeyboardEventArgs args, Action activate)
    {
        if (args.Key is "Enter" or " ")
            activate();
    }

    private void SnapshotProviderBaseline()
    {
        _savedAssignments = _assignments.ToDictionary(
            kv => kv.Key,
            kv => kv.Value.Select(a => new ProviderAssignment
            {
                Name = a.Name,
                TimeoutMs = a.TimeoutMs,
                ApiKey = a.ApiKey,
                Username = a.Username,
                Password = a.Password,
                Enabled = a.Enabled,
                Retries = a.Retries,
                LanguageStrategy = a.LanguageStrategy,
                Endpoint = a.Endpoint,
                ThrottleMs = a.ThrottleMs,
                MaxConcurrency = a.MaxConcurrency,
                Purpose = a.Purpose,
            }).ToList(),
            StringComparer.OrdinalIgnoreCase);
        _savedStrategies = new Dictionary<string, string>(_strategies, StringComparer.OrdinalIgnoreCase);
    }

    private static string SerializeAssignments(Dictionary<string, List<ProviderAssignment>> assignments) =>
        string.Join("|", assignments
            .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kv => $"{kv.Key}:{string.Join(",", kv.Value.Select(a => $"{a.Name}:{a.Enabled}:{a.TimeoutMs}:{a.ThrottleMs}:{a.MaxConcurrency}:{a.Endpoint}:{a.Retries}:{a.LanguageStrategy}:{a.Purpose}"))}"));
}
