using System.Text.Json;
using System.Text.Json.Serialization;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Api.Services.Plugins;

public sealed class ApprovedPluginCatalogService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfigurationLoader _configurationLoader;
    private readonly ILogger<ApprovedPluginCatalogService> _logger;

    public ApprovedPluginCatalogService(
        IHttpClientFactory httpClientFactory,
        IConfigurationLoader configurationLoader,
        ILogger<ApprovedPluginCatalogService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configurationLoader = configurationLoader;
        _logger = logger;
    }

    public async Task<ApprovedPluginCatalogDto> GetAsync(CancellationToken ct = default)
    {
        var settings = _configurationLoader.LoadCore().PluginCatalog;
        if (!settings.Enabled)
        {
            return new ApprovedPluginCatalogDto
            {
                SourceUrl = settings.ApprovedPluginsUrl,
                Status = "disabled",
                Message = "Approved plugin catalogue lookup is disabled in core configuration.",
            };
        }

        if (!IsAllowedGitHubRawUrl(settings.ApprovedPluginsUrl))
        {
            return new ApprovedPluginCatalogDto
            {
                SourceUrl = settings.ApprovedPluginsUrl,
                Status = "invalid_source",
                Message = "Approved plugin catalogue URL must point to raw.githubusercontent.com or github.com.",
            };
        }

        try
        {
            var client = _httpClientFactory.CreateClient("plugin_catalog");
            using var response = await client.GetAsync(settings.ApprovedPluginsUrl, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return new ApprovedPluginCatalogDto
                {
                    SourceUrl = settings.ApprovedPluginsUrl,
                    Status = "unavailable",
                    Message = $"GitHub returned {(int)response.StatusCode} {response.ReasonPhrase}.",
                };
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            var catalog = await JsonSerializer.DeserializeAsync<ApprovedPluginCatalogDto>(stream, JsonOptions, ct).ConfigureAwait(false)
                ?? new ApprovedPluginCatalogDto();

            catalog.SourceUrl = string.IsNullOrWhiteSpace(catalog.SourceUrl)
                ? settings.ApprovedPluginsUrl
                : catalog.SourceUrl;
            catalog.Status = string.IsNullOrWhiteSpace(catalog.Status) ? "ok" : catalog.Status;
            catalog.Plugins = catalog.Plugins
                .Where(p => !string.IsNullOrWhiteSpace(p.Id))
                .OrderBy(p => p.Name)
                .ThenBy(p => p.Id)
                .ToList();

            return catalog;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Approved plugin catalogue could not be fetched from {SourceUrl}", settings.ApprovedPluginsUrl);
            return new ApprovedPluginCatalogDto
            {
                SourceUrl = settings.ApprovedPluginsUrl,
                Status = "unavailable",
                Message = ex.Message,
            };
        }
    }

    private static bool IsAllowedGitHubRawUrl(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        return uri.Scheme == Uri.UriSchemeHttps
            && (string.Equals(uri.Host, "raw.githubusercontent.com", StringComparison.OrdinalIgnoreCase)
                || string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase));
    }
}

public sealed class ApprovedPluginCatalogDto
{
    [JsonPropertyName("schema_version")]
    public string SchemaVersion { get; set; } = "1.0";

    [JsonPropertyName("source_url")]
    public string SourceUrl { get; set; } = "";

    [JsonPropertyName("last_updated")]
    public DateTimeOffset? LastUpdated { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = "ok";

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("plugins")]
    public List<ApprovedPluginDto> Plugins { get; set; } = [];
}

public sealed class ApprovedPluginDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("author")]
    public string Author { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "approved";

    [JsonPropertyName("repository_url")]
    public string? RepositoryUrl { get; set; }

    [JsonPropertyName("release_url")]
    public string? ReleaseUrl { get; set; }

    [JsonPropertyName("package_url")]
    public string? PackageUrl { get; set; }

    [JsonPropertyName("sha256")]
    public string? Sha256 { get; set; }

    [JsonPropertyName("minimum_tuvima_api_version")]
    public string MinimumTuvimaApiVersion { get; set; } = "1.0.0";

    [JsonPropertyName("capabilities")]
    public List<string> Capabilities { get; set; } = [];

    [JsonPropertyName("install_notes")]
    public string? InstallNotes { get; set; }
}
