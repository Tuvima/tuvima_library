using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace MediaEngine.Providers.Services;

public sealed record AppleTrackSearchMatch(
    string CollectionId,
    JsonNode Track,
    double Score,
    bool TitleExact,
    bool ArtistExact,
    bool SingleTrackRelease,
    bool AlbumExact,
    double AlbumScore);

public sealed class AppleRetailClient
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly RetailRequestBuilder _requestBuilder;
    private readonly RetailHttpThrottle _throttle;
    private readonly ILogger<AppleRetailClient> _logger;

    public AppleRetailClient(
        IHttpClientFactory httpFactory,
        RetailRequestBuilder requestBuilder,
        RetailHttpThrottle throttle,
        ILogger<AppleRetailClient> logger)
    {
        _httpFactory = httpFactory;
        _requestBuilder = requestBuilder;
        _throttle = throttle;
        _logger = logger;
    }

    public async Task<AppleTrackSearchMatch?> SearchTrackAsync(
        string? artist,
        string? trackTitle,
        string? albumTitle,
        string country,
        string language,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(trackTitle))
            return null;

        AppleTrackSearchMatch? bestMatch = null;

        try
        {
            using var client = _httpFactory.CreateClient("apple_api");
            foreach (var searchQuery in BuildTrackSearchQueries(trackTitle, artist, albumTitle))
            {
                var url = _requestBuilder.BuildAppleTrackSearchUrl(searchQuery, country, language);

                await _throttle.ThrottleItunesAsync(ct).ConfigureAwait(false);

                var json = await client.GetFromJsonAsync<JsonNode>(url, ct).ConfigureAwait(false);
                var results = json?["results"]?.AsArray();
                if (results is null || results.Count == 0)
                    continue;

                var currentMatch = EvaluateTrackSearchResults(results, artist, trackTitle, albumTitle);
                if (currentMatch is null)
                    continue;

                if (currentMatch.TitleExact && currentMatch.ArtistExact && currentMatch.SingleTrackRelease)
                {
                    LogTrackMatch(currentMatch, artist, trackTitle);
                    return currentMatch;
                }

                if (bestMatch is null || currentMatch.Score > bestMatch.Score)
                    bestMatch = currentMatch;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "RetailMatchWorker: Apple track search failed for '{Artist}' / '{Title}'",
                artist ?? "-", trackTitle ?? "-");
            return null;
        }

        if (bestMatch is { Score: >= 0.50 })
        {
            LogTrackMatch(bestMatch, artist, trackTitle);
            return bestMatch;
        }

        _logger.LogInformation(
            "Music: Apple iTunes track search - best score {Score:F2} below threshold for '{Artist}' / '{Title}'",
            bestMatch?.Score ?? 0.0, artist ?? "-", trackTitle);
        return null;
    }

    public async Task<string?> SearchAlbumAsync(
        string? artist,
        string? album,
        string country,
        string language,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(album))
            return null;

        var url = _requestBuilder.BuildAppleAlbumSearchUrl(artist, album, country, language);

        await _throttle.ThrottleItunesAsync(ct).ConfigureAwait(false);

        try
        {
            using var client = _httpFactory.CreateClient("apple_api");
            var json = await client.GetFromJsonAsync<JsonNode>(url, ct).ConfigureAwait(false);

            var results = json?["results"]?.AsArray();
            if (results is null || results.Count == 0)
                return null;

            double bestScore = 0.0;
            string? bestCollectionId = null;

            foreach (var result in results)
            {
                if (result is null)
                    continue;

                var resultCollection = result["collectionName"]?.GetValue<string>();
                var resultArtist = result["artistName"]?.GetValue<string>();
                var resultId = result["collectionId"]?.GetValue<long?>() is { } id
                    ? id.ToString()
                    : null;

                if (string.IsNullOrWhiteSpace(resultCollection) || resultId is null)
                    continue;

                var albumScore = RetailTextSimilarity.ComputeWordOverlap(album, resultCollection);
                var artistScore = !string.IsNullOrWhiteSpace(artist) && !string.IsNullOrWhiteSpace(resultArtist)
                    ? RetailTextSimilarity.ComputeWordOverlap(artist, resultArtist)
                    : 0.0;
                var combined = albumScore * 0.7 + artistScore * 0.3;
                if (combined > bestScore)
                {
                    bestScore = combined;
                    bestCollectionId = resultId;
                }
            }

            if (bestScore >= 0.40)
            {
                _logger.LogInformation(
                    "Music: Apple iTunes album search matched collectionId={Id} (score={Score:F2}) for '{Artist}' / '{Album}'",
                    bestCollectionId, bestScore, artist ?? "-", album ?? "-");
                return bestCollectionId;
            }

            _logger.LogInformation(
                "Music: Apple iTunes album search - best score {Score:F2} below threshold for '{Artist}' / '{Album}'",
                bestScore, artist ?? "-", album ?? "-");
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "RetailMatchWorker: Apple album search failed for '{Artist}' / '{Album}'",
                artist ?? "-", album ?? "-");
            return null;
        }
    }

    public async Task<IReadOnlyList<JsonNode>> FetchAlbumTracksAsync(
        string collectionId,
        string country,
        string language,
        CancellationToken ct)
    {
        var url = _requestBuilder.BuildAppleAlbumLookupUrl(collectionId, country, language);

        await _throttle.ThrottleItunesAsync(ct).ConfigureAwait(false);

        try
        {
            using var client = _httpFactory.CreateClient("apple_api");
            var json = await client.GetFromJsonAsync<JsonNode>(url, ct).ConfigureAwait(false);

            var results = json?["results"]?.AsArray();
            if (results is null || results.Count == 0)
                return [];

            var tracks = new List<JsonNode>();
            foreach (var node in results)
            {
                if (node is null)
                    continue;

                var wrapperType = node["wrapperType"]?.GetValue<string>();
                if (string.Equals(wrapperType, "track", StringComparison.OrdinalIgnoreCase))
                    tracks.Add(node);
            }

            return tracks;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "RetailMatchWorker: Apple album track lookup failed for collectionId={Id}", collectionId);
            return [];
        }
    }

    public static IReadOnlyList<string> BuildTrackSearchQueries(
        string trackTitle,
        string? artist,
        string? albumTitle)
    {
        var queries = new List<string>
        {
            string.Join(' ', new[] { trackTitle, artist }.Where(v => !string.IsNullOrWhiteSpace(v)))
        };

        if (!string.IsNullOrWhiteSpace(albumTitle))
        {
            queries.Add(string.Join(' ', new[] { trackTitle, artist, albumTitle }
                .Where(v => !string.IsNullOrWhiteSpace(v))));
        }

        return queries
            .Where(q => !string.IsNullOrWhiteSpace(q))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static AppleTrackSearchMatch? EvaluateTrackSearchResults(
        JsonArray results,
        string? artist,
        string trackTitle,
        string? albumTitle)
    {
        double bestScore = 0.0;
        string? bestCollectionId = null;
        JsonNode? bestTrack = null;
        var bestTitleExact = false;
        var bestArtistExact = false;
        var bestSingleTrackRelease = false;
        var bestAlbumExact = false;
        var bestAlbumScore = 0.0;

        foreach (var result in results)
        {
            if (result is null)
                continue;

            var resultTrackName = result["trackName"]?.GetValue<string>();
            var resultArtist = result["artistName"]?.GetValue<string>();
            var resultAlbum = result["collectionName"]?.GetValue<string>();
            var resultTrackCount = result["trackCount"]?.GetValue<long?>();
            var resultId = result["collectionId"]?.GetValue<long?>() is { } id
                ? id.ToString()
                : null;

            if (string.IsNullOrWhiteSpace(resultTrackName) || resultId is null)
                continue;

            var titleScore = RetailTextSimilarity.ComputeWordOverlap(trackTitle, resultTrackName);
            var artistScore = !string.IsNullOrWhiteSpace(artist) && !string.IsNullOrWhiteSpace(resultArtist)
                ? RetailTextSimilarity.ComputeWordOverlap(artist, resultArtist)
                : 0.0;
            var albumScore = !string.IsNullOrWhiteSpace(albumTitle) && !string.IsNullOrWhiteSpace(resultAlbum)
                ? RetailTextSimilarity.ComputeWordOverlap(albumTitle, resultAlbum)
                : 0.0;
            var titleExact = RetailTextSimilarity.AreEquivalentNames(trackTitle, resultTrackName);
            var artistExact = RetailTextSimilarity.AreEquivalentNames(artist, resultArtist);
            var albumExact = RetailTextSimilarity.AreEquivalentNames(albumTitle, resultAlbum);
            var singleTrackRelease = resultTrackCount == 1;

            var combined = string.IsNullOrWhiteSpace(albumTitle)
                ? titleScore * 0.65 + artistScore * 0.35
                : titleScore * 0.50 + artistScore * 0.25 + albumScore * 0.25;

            if (titleExact)
                combined += 0.10;

            if (artistExact)
                combined += 0.15;

            if (titleExact && artistExact && singleTrackRelease)
                combined += 0.20;

            combined = Math.Clamp(combined, 0.0, 1.0);
            if (combined > bestScore)
            {
                bestScore = combined;
                bestCollectionId = resultId;
                bestTrack = result;
                bestTitleExact = titleExact;
                bestArtistExact = artistExact;
                bestSingleTrackRelease = singleTrackRelease;
                bestAlbumExact = albumExact;
                bestAlbumScore = albumScore;
            }
        }

        return bestScore >= 0.50 && bestCollectionId is not null && bestTrack is not null
            ? new AppleTrackSearchMatch(
                bestCollectionId,
                bestTrack,
                bestScore,
                bestTitleExact,
                bestArtistExact,
                bestSingleTrackRelease,
                bestAlbumExact,
                bestAlbumScore)
            : null;
    }

    private void LogTrackMatch(AppleTrackSearchMatch match, string? artist, string? trackTitle)
    {
        var trackName = match.Track["trackName"]?.GetValue<string>() ?? "(unknown)";
        _logger.LogInformation(
            "Music: Apple iTunes track search matched '{TrackName}' -> collectionId={Id} (score={Score:F2}) for '{Artist}' / '{Title}'",
            trackName, match.CollectionId, match.Score, artist ?? "-", trackTitle);
    }
}
