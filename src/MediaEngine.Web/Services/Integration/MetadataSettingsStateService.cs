using MediaEngine.Contracts.Settings;
using MediaEngine.Domain.Configuration;
using MediaEngine.Web.Components.Shared;
using MudBlazor;
using PipelineConfiguration = MediaEngine.Contracts.Settings.PipelineConfiguration;
using PipelineProviderEntry = MediaEngine.Contracts.Settings.PipelineProviderEntry;

namespace MediaEngine.Web.Services.Integration;

public sealed class MetadataSettingsStateService
{
    public static readonly string[] MediaTypeOrder = ["Books", "Audiobooks", "Comics", "Movies", "TV", "Music"];

    private readonly IEngineApiClient _api;
    private readonly ProviderCatalogueService _catalogueService;

    public MetadataSettingsStateService(IEngineApiClient api, ProviderCatalogueService catalogueService)
    {
        _api = api;
        _catalogueService = catalogueService;
    }

    public async Task<MetadataSettingsSnapshot> LoadAsync(CancellationToken ct = default)
    {
        var catalogueTask = _catalogueService.GetCatalogueAsync(ct);
        var statusesTask = _api.GetProviderStatusAsync(ct);
        var pipelinesTask = _api.GetPipelinesAsync(ct);
        var hydrationTask = _api.GetHydrationSettingsAsync(ct);
        await Task.WhenAll(catalogueTask, statusesTask, pipelinesTask, hydrationTask);

        var catalogue = await catalogueTask;
        var statuses = (await statusesTask).ToDictionary(status => status.Name, StringComparer.OrdinalIgnoreCase);
        var pipelines = await pipelinesTask ?? new PipelineConfiguration();
        var hydration = await hydrationTask;
        var mediaPipelines = MediaTypeOrder
            .Select(mediaType => BuildPipeline(mediaType, pipelines, catalogue, statuses))
            .ToList();

        return new MetadataSettingsSnapshot(
            mediaPipelines,
            BuildEnrichment(catalogue, statuses, hydration),
            catalogue.FirstOrDefault(provider => string.Equals(provider.Name, "wikidata_reconciliation", StringComparison.OrdinalIgnoreCase)),
            statuses,
            hydration);
    }

    public static string NormalizeMediaType(string? value)
    {
        var normalized = new string((value ?? string.Empty).Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        return normalized switch
        {
            "book" or "books" => "Books",
            "audiobook" or "audiobooks" => "Audiobooks",
            "comic" or "comics" => "Comics",
            "movie" or "movies" => "Movies",
            "tv" or "tvshow" or "tvshows" => "TV",
            "music" or "song" or "songs" => "Music",
            _ => string.Empty,
        };
    }

    public static string MediaTypeSlug(string mediaType) => NormalizeMediaType(mediaType) switch
    {
        "Books" => "books",
        "Audiobooks" => "audiobooks",
        "Comics" => "comics",
        "Movies" => "movies",
        "TV" => "tv",
        "Music" => "music",
        _ => string.Empty,
    };

    public static string MediaTypeLabel(string mediaType) => NormalizeMediaType(mediaType) == "TV" ? "TV Shows" : NormalizeMediaType(mediaType);

    public static string MediaTypeIcon(string mediaType) => NormalizeMediaType(mediaType) switch
    {
        "Books" => Icons.Material.Outlined.MenuBook,
        "Audiobooks" => Icons.Material.Outlined.Headphones,
        "Comics" => Icons.Material.Outlined.AutoStories,
        "Movies" => Icons.Material.Outlined.Movie,
        "TV" => Icons.Material.Outlined.Tv,
        "Music" => Icons.Material.Outlined.MusicNote,
        _ => Icons.Material.Outlined.Category,
    };

    public static string LaneFor(string mediaType) => NormalizeMediaType(mediaType) switch
    {
        "Books" or "Audiobooks" or "Comics" => "Read",
        "Movies" or "TV" => "Watch",
        "Music" => "Listen",
        _ => "Other",
    };

    public static string ContextualProviderName(string providerKey, string mediaType, string fallback) =>
        string.Equals(providerKey, "apple_api", StringComparison.OrdinalIgnoreCase)
            ? NormalizeMediaType(mediaType) == "Music" ? "Apple Music" : "Apple Books"
            : fallback;

    private MetadataMediaPipeline BuildPipeline(
        string mediaType,
        PipelineConfiguration configuration,
        IReadOnlyList<ProviderCatalogueDto> catalogue,
        IReadOnlyDictionary<string, ProviderStatusDto> statuses)
    {
        var pipeline = configuration.GetPipelineForMediaType(mediaType);
        var providers = pipeline.Providers
            .OrderBy(provider => provider.Rank)
            .Select((provider, index) => BuildParticipant(mediaType, provider, index, catalogue, statuses))
            .ToList();

        return new MetadataMediaPipeline(
            mediaType,
            MediaTypeLabel(mediaType),
            LaneFor(mediaType),
            MediaTypeIcon(mediaType),
            DescriptionFor(mediaType),
            pipeline.Strategy.ToString(),
            providers);
    }

    private MetadataProviderParticipant BuildParticipant(
        string mediaType,
        PipelineProviderEntry entry,
        int index,
        IReadOnlyList<ProviderCatalogueDto> catalogue,
        IReadOnlyDictionary<string, ProviderStatusDto> statuses)
    {
        var catalog = catalogue.FirstOrDefault(provider => string.Equals(provider.Name, entry.Name, StringComparison.OrdinalIgnoreCase));
        statuses.TryGetValue(entry.Name, out var status);
        var rawName = catalog?.DisplayName ?? status?.DisplayName ?? _catalogueService.GetDisplayName(entry.Name);
        var name = ContextualProviderName(entry.Name, mediaType, rawName);
        var role = RoleLabel(entry, index);
        var capabilities = ContributionLabels(mediaType, catalog, status, entry);
        var logo = catalog?.IconPath;
        if (!string.IsNullOrWhiteSpace(logo) && !logo.StartsWith('/')) logo = "/" + logo.TrimStart('/');
        var accent = catalog?.AccentColor ?? _catalogueService.GetAccentColor(entry.Name);
        var icon = _catalogueService.GetAccent(entry.Name, catalog?.MaterialIcon ?? status?.CustomIconName).Icon;

        return new MetadataProviderParticipant(
            entry.Name,
            name,
            role,
            entry.Purpose,
            entry.UseAsIdentityFallback,
            status?.Enabled ?? catalog?.Enabled == true,
            HealthLabel(status),
            HealthTone(status),
            logo,
            icon,
            accent,
            capabilities,
            catalog,
            status);
    }

    private static IReadOnlyList<MetadataEnrichmentCapability> BuildEnrichment(
        IReadOnlyList<ProviderCatalogueDto> catalogue,
        IReadOnlyDictionary<string, ProviderStatusDto> statuses,
        HydrationSettingsDto? hydration)
    {
        return
        [
            Enrichment("artwork", "Artwork", "Adds additional backgrounds and logos after identification.", "fanart_tv", ["Movies", "TV", "Music"], Icons.Material.Outlined.Image, catalogue, statuses, hydration),
            Enrichment("lyrics", "Lyrics", "Adds synchronized and plain-text lyrics for music.", "lrclib", ["Music"], Icons.Material.Outlined.Lyrics, catalogue, statuses, hydration),
            Enrichment("subtitles", "Subtitles", "Finds subtitles and stores normalized text tracks for video.", "opensubtitles", ["Movies", "TV"], Icons.Material.Outlined.Subtitles, catalogue, statuses, hydration),
            Enrichment("people", "People", "Adds contributor biographies, portraits, relationships, and roles.", "wikidata_reconciliation", ["Read", "Watch", "Listen"], Icons.Material.Outlined.People, catalogue, statuses, hydration),
        ];
    }

    private static MetadataEnrichmentCapability Enrichment(
        string id,
        string title,
        string description,
        string providerKey,
        IReadOnlyList<string> appliesTo,
        string icon,
        IReadOnlyList<ProviderCatalogueDto> catalogue,
        IReadOnlyDictionary<string, ProviderStatusDto> statuses,
        HydrationSettingsDto? hydration)
    {
        var provider = catalogue.FirstOrDefault(candidate => string.Equals(candidate.Name, providerKey, StringComparison.OrdinalIgnoreCase));
        statuses.TryGetValue(providerKey, out var status);
        var enabled = status?.Enabled ?? provider?.Enabled == true;
        if (id is "artwork" or "people") enabled &= hydration?.Stage3Enabled == true;
        return new MetadataEnrichmentCapability(id, title, description, providerKey,
            provider?.DisplayName ?? providerKey, appliesTo, icon, enabled,
            HealthLabel(status), HealthTone(status), provider, status);
    }

    private static IReadOnlyList<string> ContributionLabels(
        string mediaType,
        ProviderCatalogueDto? catalogue,
        ProviderStatusDto? status,
        PipelineProviderEntry entry)
    {
        var contributions = new List<string>();
        if (string.Equals(entry.Purpose, "identity", StringComparison.OrdinalIgnoreCase) || entry.UseAsIdentityFallback)
            contributions.Add("Identity");
        var capabilities = catalogue?.Capabilities ?? [];
        if (capabilities.Contains(ProviderCapabilityId.Metadata, StringComparer.OrdinalIgnoreCase))
            contributions.Add(NormalizeMediaType(mediaType) switch { "Comics" => "Issue Details", "Music" => "Album Details", "TV" => "Episode Details", _ => "Details" });
        if (capabilities.Contains(ProviderCapabilityId.Artwork, StringComparer.OrdinalIgnoreCase))
        {
            contributions.Add(NormalizeMediaType(mediaType) is "Movies" or "TV" ? "Poster" : "Cover");
            if (NormalizeMediaType(mediaType) is "Movies" or "TV") contributions.Add("Background");
        }
        if (capabilities.Contains(ProviderCapabilityId.Ratings, StringComparer.OrdinalIgnoreCase)) contributions.Add("Ratings");
        if (capabilities.Contains(ProviderCapabilityId.People, StringComparer.OrdinalIgnoreCase) && NormalizeMediaType(mediaType) is "Movies" or "TV") contributions.Add("Cast Seeds");
        if (contributions.Count == 0 && status?.AvailableFields is { Count: > 0 }) contributions.Add("Metadata");
        return contributions.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string RoleLabel(PipelineProviderEntry entry, int index)
    {
        if (entry.UseAsIdentityFallback) return "Identity fallback & metadata assistant";
        if (string.Equals(entry.Purpose, "enrichment", StringComparison.OrdinalIgnoreCase)) return "Additional metadata assistant";
        if (index == 0 && string.Equals(entry.Purpose, "identity", StringComparison.OrdinalIgnoreCase)) return "Primary identity provider";
        if (string.Equals(entry.Purpose, "identity", StringComparison.OrdinalIgnoreCase)) return "Additional identity source";
        return "Pipeline participant";
    }

    private static string DescriptionFor(string mediaType) => NormalizeMediaType(mediaType) switch
    {
        "Books" => "Identity, book details, and cover artwork.",
        "Audiobooks" => "Identity, audiobook details, and cover artwork.",
        "Comics" => "Identity, issue details, and cover artwork.",
        "Movies" => "Identity, movie details, poster, and background.",
        "TV" => "Identity, episode details, poster, and background.",
        "Music" => "Track and album identity, details, and cover artwork.",
        _ => "Media identification and initial metadata.",
    };

    public static string HealthLabel(ProviderStatusDto? status)
    {
        if (status is null) return "Not checked";
        if (!status.Enabled) return "Disabled";
        if (status.RequiresApiKey && !status.HasApiKey) return "Authentication required";
        if (string.Equals(status.HealthStatus, "Down", StringComparison.OrdinalIgnoreCase)) return "Unavailable";
        if (string.Equals(status.HealthStatus, "Degraded", StringComparison.OrdinalIgnoreCase)) return "Degraded";
        return status.IsReachable || string.Equals(status.HealthStatus, "Healthy", StringComparison.OrdinalIgnoreCase) ? "Connected" : "Enabled";
    }

    public static AppUiTone HealthTone(ProviderStatusDto? status) => HealthLabel(status) switch
    {
        "Connected" => AppUiTone.Success,
        "Enabled" => AppUiTone.Success,
        "Degraded" or "Authentication required" => AppUiTone.Warning,
        "Unavailable" => AppUiTone.Error,
        _ => AppUiTone.Neutral,
    };
}

public sealed record MetadataSettingsSnapshot(
    IReadOnlyList<MetadataMediaPipeline> MediaPipelines,
    IReadOnlyList<MetadataEnrichmentCapability> Enrichment,
    ProviderCatalogueDto? Wikidata,
    IReadOnlyDictionary<string, ProviderStatusDto> Statuses,
    HydrationSettingsDto? Hydration)
{
    public MetadataMediaPipeline? PipelineFor(string? mediaType) => MediaPipelines.FirstOrDefault(pipeline =>
        string.Equals(pipeline.MediaType, MetadataSettingsStateService.NormalizeMediaType(mediaType), StringComparison.OrdinalIgnoreCase));
}

public sealed record MetadataMediaPipeline(
    string MediaType,
    string Label,
    string Lane,
    string Icon,
    string Description,
    string Strategy,
    IReadOnlyList<MetadataProviderParticipant> Providers);

public sealed record MetadataProviderParticipant(
    string Key,
    string DisplayName,
    string RoleLabel,
    string? Purpose,
    bool IsIdentityFallback,
    bool Enabled,
    string HealthLabel,
    AppUiTone HealthTone,
    string? LogoUrl,
    string Icon,
    string AccentColor,
    IReadOnlyList<string> Contributions,
    ProviderCatalogueDto? Catalogue,
    ProviderStatusDto? Status);

public sealed record MetadataEnrichmentCapability(
    string Id,
    string Title,
    string Description,
    string ProviderKey,
    string ProviderName,
    IReadOnlyList<string> AppliesTo,
    string Icon,
    bool Enabled,
    string HealthLabel,
    AppUiTone HealthTone,
    ProviderCatalogueDto? Catalogue,
    ProviderStatusDto? Status);
