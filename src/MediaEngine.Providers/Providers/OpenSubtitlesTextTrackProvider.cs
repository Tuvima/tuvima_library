using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MediaEngine.Domain;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Enums;
using MediaEngine.Storage.Models;
using Microsoft.Extensions.Logging;

namespace MediaEngine.Providers.Providers;

public sealed class OpenSubtitlesTextTrackProvider : ITextTrackProvider
{
    private readonly ProviderConfiguration _config;
    private readonly IHttpClientFactory _httpFactory;
    private readonly IProviderResponseCacheRepository _cache;
    private readonly IProviderHealthMonitor _health;
    private readonly ILogger<OpenSubtitlesTextTrackProvider> _logger;
    private readonly SemaphoreSlim _loginLock = new(1, 1);
    private string? _bearerToken;
    private DateTimeOffset _bearerTokenExpiresAt;

    public OpenSubtitlesTextTrackProvider(
        ProviderConfiguration config,
        IHttpClientFactory httpFactory,
        IProviderResponseCacheRepository cache,
        IProviderHealthMonitor health,
        ILogger<OpenSubtitlesTextTrackProvider> logger)
    {
        _config = config;
        _httpFactory = httpFactory;
        _cache = cache;
        _health = health;
        _logger = logger;
    }

    public string Name => _config.Name;

    public TextTrackKind Kind => TextTrackKind.Subtitles;

    public bool IsEnabled => _config.Enabled;

    public bool CanHandle(MediaType mediaType) => mediaType is MediaType.Movies or MediaType.TV;

    public async Task<IReadOnlyList<TextTrackCandidate>> SearchAsync(TextTrackLookup lookup, CancellationToken ct = default)
    {
        if (!IsEnabled || !CanHandle(lookup.MediaType) || !HasCredentials())
            return [];

        var baseUrl = _config.Endpoints.GetValueOrDefault("api")?.TrimEnd('/') ?? "https://api.opensubtitles.com/api/v1";
        var language = NormalizeLanguage(lookup.Language);
        var query = new List<string> { $"languages={Uri.EscapeDataString(language)}", "order_by=download_count" };
        if (lookup.BridgeIds.TryGetValue(BridgeIdKeys.ImdbId, out var imdb) && !string.IsNullOrWhiteSpace(imdb))
            query.Add($"imdb_id={Uri.EscapeDataString(imdb.TrimStart('t'))}");
        else if (!string.IsNullOrWhiteSpace(lookup.Title))
            query.Add($"query={Uri.EscapeDataString(lookup.Title)}");

        if (lookup.MediaType == MediaType.TV)
        {
            if (lookup.BridgeIds.TryGetValue(MetadataFieldConstants.SeasonNumber, out var season))
                query.Add($"season_number={Uri.EscapeDataString(season)}");
            if (lookup.BridgeIds.TryGetValue(MetadataFieldConstants.EpisodeNumber, out var episode))
                query.Add($"episode_number={Uri.EscapeDataString(episode)}");
        }

        var url = $"{baseUrl}/subtitles?{string.Join("&", query)}";
        var json = await GetJsonAsync(url, ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json))
            return [];

        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            return [];

        var candidates = new List<TextTrackCandidate>();
        foreach (var item in data.EnumerateArray().Take(5))
        {
            var attributes = item.TryGetProperty("attributes", out var attrs) ? attrs : item;
            var files = attributes.TryGetProperty("files", out var filesNode) && filesNode.ValueKind == JsonValueKind.Array
                ? filesNode.EnumerateArray().ToList()
                : [];
            if (files.Count == 0)
                continue;

            var file = files[0];
            var fileId = file.TryGetProperty("file_id", out var fileIdNode) ? fileIdNode.ToString() : null;
            if (string.IsNullOrWhiteSpace(fileId))
                continue;

            var lang = attributes.TryGetProperty("language", out var langNode) ? langNode.GetString() ?? language : language;
            var hearingImpaired = attributes.TryGetProperty("hearing_impaired", out var hiNode) && hiNode.ValueKind is JsonValueKind.True;
            var downloads = attributes.TryGetProperty("download_count", out var downloadsNode) && downloadsNode.TryGetInt32(out var dc) ? dc : 0;
            var confidence = Math.Clamp(0.72 + Math.Min(downloads, 1000) / 10000d, 0.72, 0.9);

            candidates.Add(new TextTrackCandidate(
                Provider: Name,
                Kind: TextTrackKind.Subtitles,
                SourceId: fileId,
                SourceUrl: url,
                Language: lang,
                SourceFormat: "srt",
                Confidence: confidence,
                IsHearingImpaired: hearingImpaired,
                DurationMatchScore: null,
                Payload: fileId));
        }

        return candidates;
    }

    public async Task<TextTrackDownload?> DownloadAsync(TextTrackCandidate candidate, CancellationToken ct = default)
    {
        if (!HasCredentials())
            return null;

        var baseUrl = _config.Endpoints.GetValueOrDefault("api")?.TrimEnd('/') ?? "https://api.opensubtitles.com/api/v1";
        using var client = _httpFactory.CreateClient(Name);
        await ApplyHeadersAsync(client, baseUrl, ct).ConfigureAwait(false);

        using var response = await client.PostAsJsonAsync($"{baseUrl}/download", new { file_id = candidate.SourceId }, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            await _health.ReportFailureAsync(Name, $"download failed: {(int)response.StatusCode}", ct).ConfigureAwait(false);
            return null;
        }

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        if (!doc.RootElement.TryGetProperty("link", out var linkNode) || linkNode.ValueKind != JsonValueKind.String)
            return null;

        var link = linkNode.GetString();
        if (string.IsNullOrWhiteSpace(link))
            return null;

        var content = await client.GetStringAsync(link, ct).ConfigureAwait(false);
        await _health.ReportSuccessAsync(Name, ct).ConfigureAwait(false);
        return new TextTrackDownload(candidate, content, "srt", "vtt");
    }

    private async Task<string?> GetJsonAsync(string url, CancellationToken ct)
    {
        var queryHash = ComputeHash(url);
        var cacheKey = $"{Name}:{queryHash}";
        if (_config.CacheTtlHours is > 0)
        {
            var cached = await _cache.FindAsync(cacheKey, ct).ConfigureAwait(false);
            if (cached is not null)
                return cached.ResponseJson;
        }

        if (_health.IsDown(Name))
            return null;

        try
        {
            using var client = _httpFactory.CreateClient(Name);
            await ApplyHeadersAsync(client, _config.Endpoints.GetValueOrDefault("api")?.TrimEnd('/') ?? "https://api.opensubtitles.com/api/v1", ct).ConfigureAwait(false);
            var json = await client.GetStringAsync(url, ct).ConfigureAwait(false);
            if (_config.CacheTtlHours is > 0)
                await _cache.UpsertAsync(cacheKey, WellKnownProviders.OpenSubtitles.ToString(), queryHash, json, null, _config.CacheTtlHours.Value, ct).ConfigureAwait(false);
            await _health.ReportSuccessAsync(Name, ct).ConfigureAwait(false);
            return json;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex, "{Provider} subtitle lookup failed", Name);
            await _health.ReportFailureAsync(Name, ex.Message, ct).ConfigureAwait(false);
            return null;
        }
    }

    private async Task ApplyHeadersAsync(HttpClient client, string baseUrl, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(_config.HttpClient?.ApiKey))
            client.DefaultRequestHeaders.TryAddWithoutValidation("Api-Key", _config.HttpClient.ApiKey);

        var token = await ResolveBearerTokenAsync(baseUrl, ct).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(token))
            client.DefaultRequestHeaders.Authorization = new("Bearer", token);
    }

    private bool HasCredentials() => !_config.RequiresApiKey || !string.IsNullOrWhiteSpace(_config.HttpClient?.ApiKey);

    private async Task<string?> ResolveBearerTokenAsync(string baseUrl, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_config.HttpClient?.Username)
            || string.IsNullOrWhiteSpace(_config.HttpClient?.Password))
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(_bearerToken) && _bearerTokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(5))
            return _bearerToken;

        await _loginLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!string.IsNullOrWhiteSpace(_bearerToken) && _bearerTokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(5))
                return _bearerToken;

            using var client = _httpFactory.CreateClient(Name);
            if (!string.IsNullOrWhiteSpace(_config.HttpClient?.ApiKey))
                client.DefaultRequestHeaders.TryAddWithoutValidation("Api-Key", _config.HttpClient.ApiKey);

            using var response = await client.PostAsJsonAsync(
                $"{baseUrl}/login",
                new { username = _config.HttpClient!.Username, password = _config.HttpClient.Password },
                ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                await _health.ReportFailureAsync(Name, $"login failed: {(int)response.StatusCode}", ct).ConfigureAwait(false);
                return null;
            }

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
            if (!doc.RootElement.TryGetProperty("token", out var tokenNode) || tokenNode.ValueKind != JsonValueKind.String)
                return null;

            _bearerToken = tokenNode.GetString();
            _bearerTokenExpiresAt = DateTimeOffset.UtcNow.AddHours(23);
            return _bearerToken;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex, "{Provider} login failed", Name);
            await _health.ReportFailureAsync(Name, ex.Message, ct).ConfigureAwait(false);
            return null;
        }
        finally
        {
            _loginLock.Release();
        }
    }

    private static string NormalizeLanguage(string? language) =>
        string.IsNullOrWhiteSpace(language) ? "en" : language.Split('-', StringSplitOptions.RemoveEmptyEntries)[0].ToLowerInvariant();

    private static string ComputeHash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
