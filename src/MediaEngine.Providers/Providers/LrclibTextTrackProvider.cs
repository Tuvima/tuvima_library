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

public sealed class LrclibTextTrackProvider : ITextTrackProvider
{
    private readonly ProviderConfiguration _config;
    private readonly IHttpClientFactory _httpFactory;
    private readonly IProviderResponseCacheRepository _cache;
    private readonly IProviderHealthMonitor _health;
    private readonly ILogger<LrclibTextTrackProvider> _logger;

    public LrclibTextTrackProvider(
        ProviderConfiguration config,
        IHttpClientFactory httpFactory,
        IProviderResponseCacheRepository cache,
        IProviderHealthMonitor health,
        ILogger<LrclibTextTrackProvider> logger)
    {
        _config = config;
        _httpFactory = httpFactory;
        _cache = cache;
        _health = health;
        _logger = logger;
    }

    public string Name => _config.Name;

    public TextTrackKind Kind => TextTrackKind.Lyrics;

    public bool IsEnabled => _config.Enabled;

    public bool CanHandle(MediaType mediaType) => mediaType == MediaType.Music;

    public async Task<IReadOnlyList<TextTrackCandidate>> SearchAsync(TextTrackLookup lookup, CancellationToken ct = default)
    {
        if (!IsEnabled || !CanHandle(lookup.MediaType) || string.IsNullOrWhiteSpace(lookup.Title))
            return [];

        var baseUrl = _config.Endpoints.GetValueOrDefault("api")?.TrimEnd('/') ?? "https://lrclib.net";
        var query = new List<string>
        {
            $"track_name={Uri.EscapeDataString(lookup.Title)}",
        };
        if (!string.IsNullOrWhiteSpace(lookup.Artist))
            query.Add($"artist_name={Uri.EscapeDataString(lookup.Artist)}");
        if (!string.IsNullOrWhiteSpace(lookup.Album))
            query.Add($"album_name={Uri.EscapeDataString(lookup.Album)}");
        if (lookup.DurationSeconds is > 0)
            query.Add($"duration={Math.Round(lookup.DurationSeconds.Value).ToString(System.Globalization.CultureInfo.InvariantCulture)}");

        var url = $"{baseUrl}/api/get?{string.Join("&", query)}";
        var json = await GetJsonAsync(url, ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json))
            return [];

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("syncedLyrics", out var synced) || synced.ValueKind != JsonValueKind.String)
            return [];

        var lyrics = synced.GetString();
        if (string.IsNullOrWhiteSpace(lyrics))
            return [];

        var sourceId = root.TryGetProperty("id", out var id) ? id.ToString() : ComputeHash(url);
        var durationScore = ScoreDuration(root, lookup.DurationSeconds);
        return
        [
            new TextTrackCandidate(
                Provider: Name,
                Kind: TextTrackKind.Lyrics,
                SourceId: sourceId,
                SourceUrl: url,
                Language: lookup.Language ?? "und",
                SourceFormat: "lrc",
                Confidence: durationScore.HasValue ? Math.Max(0.75, durationScore.Value) : 0.82,
                IsHearingImpaired: false,
                DurationMatchScore: durationScore,
                Payload: lyrics)
        ];
    }

    public Task<TextTrackDownload?> DownloadAsync(TextTrackCandidate candidate, CancellationToken ct = default)
    {
        var content = candidate.Payload as string;
        return Task.FromResult(string.IsNullOrWhiteSpace(content)
            ? null
            : new TextTrackDownload(candidate, content, "lrc", "lrc"));
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
            var json = await client.GetStringAsync(url, ct).ConfigureAwait(false);
            if (_config.CacheTtlHours is > 0)
                await _cache.UpsertAsync(cacheKey, WellKnownProviders.Lrclib.ToString(), queryHash, json, null, _config.CacheTtlHours.Value, ct).ConfigureAwait(false);
            await _health.ReportSuccessAsync(Name, ct).ConfigureAwait(false);
            return json;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex, "{Provider} lyrics lookup failed", Name);
            await _health.ReportFailureAsync(Name, ex.Message, ct).ConfigureAwait(false);
            return null;
        }
    }

    private static double? ScoreDuration(JsonElement root, double? expected)
    {
        if (expected is not > 0 || !root.TryGetProperty("duration", out var duration) || !duration.TryGetDouble(out var actual))
            return null;
        var delta = Math.Abs(actual - expected.Value);
        return Math.Clamp(1d - (delta / 10d), 0d, 1d);
    }

    private static string ComputeHash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
