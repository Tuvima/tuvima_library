using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using MediaEngine.Domain;
using MediaEngine.Domain.Constants;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Models;
using MediaEngine.Domain.Services;
using MediaEngine.Intelligence.Contracts;
using MediaEngine.Providers.Contracts;
using MediaEngine.Providers.Helpers;
using MediaEngine.Providers.Models;
using MediaEngine.Providers.Services;
using MediaEngine.Storage.Contracts;
using MediaEngine.Storage.Models;
using MediaEngine.Storage.Services;
using Microsoft.Extensions.Logging;

namespace MediaEngine.Providers.Workers;

/// <summary>
/// Stage 1: Retail Identification.
/// Leases <see cref="IdentityJobState.Queued"/> jobs, runs retail providers
/// per the configured strategy, scores candidates, and persists evidence.
///
/// Music and TV jobs are processed at album/show level rather than per-track/episode:
/// one API call fetches the full album (Apple) or season episode list (TMDB), then
/// each sibling job in the batch receives its per-item claims without additional calls.
///
/// This is a plain service — the Api layer wraps it in a <c>BackgroundService</c>
/// for polling lifecycle management.
/// </summary>
public sealed class RetailMatchWorker
{
    private readonly IIdentityJobRepository _jobRepo;
    private readonly IRetailCandidateRepository _candidateRepo;
    private readonly StageOutcomeFactory _outcomeFactory;
    private readonly TimelineRecorder _timeline;
    private readonly Services.BatchProgressService _batchProgress;
    private readonly IEnumerable<IExternalMetadataProvider> _providers;
    private readonly IRetailMatchScoringService _retailScoring;
    private readonly IMetadataClaimRepository _claimRepo;
    private readonly ICanonicalValueRepository _canonicalRepo;
    private readonly IScoringEngine _scoringEngine;
    private readonly IConfigurationLoader _configLoader;
    private readonly IBridgeIdRepository _bridgeIdRepo;
    private readonly IWorkRepository _workRepo;
    private readonly WorkClaimRouter _claimRouter;
    private readonly IHttpClientFactory _httpFactory;
    private readonly PostPipelineService _postPipeline;
    private readonly IEnrichmentConcurrencyLimiter _concurrency;
    private readonly IEntityAssetRepository? _entityAssetRepo;
    private readonly IImageCacheRepository? _imageCache;
    private readonly AssetPathService? _assetPaths;
    private readonly IAssetExportService? _assetExportService;
    private readonly ILogger<RetailMatchWorker> _logger;

    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Cross-file batching window. Sourced from
    /// <c>config/core.json → pipeline.lease_sizes.retail</c> at construction time.
    /// Larger values mean a single drop of N files (e.g. a TV season, an album)
    /// processes in one lease cycle instead of being chopped into multiple leases
    /// — which is what enables one Apple album call to cover all its tracks.
    /// </summary>
    private readonly int _batchSize;

    // iTunes throttle: 300 ms between calls (same as config). We track it here
    // for the direct HTTP calls made by the group processors.
    private DateTime _itunesLastCallUtc = DateTime.MinValue;
    private readonly SemaphoreSlim _itunesThrottle = new(1, 1);

    // TMDB throttle: 250 ms between calls.
    private DateTime _tmdbLastCallUtc = DateTime.MinValue;
    private readonly SemaphoreSlim _tmdbThrottle = new(1, 1);

    public RetailMatchWorker(
        IIdentityJobRepository jobRepo,
        IRetailCandidateRepository candidateRepo,
        StageOutcomeFactory outcomeFactory,
        TimelineRecorder timeline,
        Services.BatchProgressService batchProgress,
        IEnumerable<IExternalMetadataProvider> providers,
        IRetailMatchScoringService retailScoring,
        IMetadataClaimRepository claimRepo,
        ICanonicalValueRepository canonicalRepo,
        IScoringEngine scoringEngine,
        IConfigurationLoader configLoader,
        IBridgeIdRepository bridgeIdRepo,
        IWorkRepository workRepo,
        WorkClaimRouter claimRouter,
        IHttpClientFactory httpFactory,
        PostPipelineService postPipeline,
        ILogger<RetailMatchWorker> logger,
        IEnrichmentConcurrencyLimiter? concurrencyLimiter = null,
        IEntityAssetRepository? entityAssetRepo = null,
        IImageCacheRepository? imageCache = null,
        AssetPathService? assetPaths = null,
        IAssetExportService? assetExportService = null)
    {
        _jobRepo = jobRepo;
        _candidateRepo = candidateRepo;
        _outcomeFactory = outcomeFactory;
        _timeline = timeline;
        _batchProgress = batchProgress;
        _providers = providers;
        _retailScoring = retailScoring;
        _claimRepo = claimRepo;
        _canonicalRepo = canonicalRepo;
        _scoringEngine = scoringEngine;
        _configLoader = configLoader;
        _bridgeIdRepo = bridgeIdRepo;
        _workRepo = workRepo;
        _claimRouter = claimRouter;
        _httpFactory = httpFactory;
        _postPipeline = postPipeline;
        _concurrency = concurrencyLimiter ?? NoopEnrichmentConcurrencyLimiter.Instance;
        _entityAssetRepo = entityAssetRepo;
        _imageCache = imageCache;
        _assetPaths = assetPaths;
        _assetExportService = assetExportService;
        _logger = logger;

        // Lease size is read once at construction. A restart applies any
        // config change — same lifetime as every other CoreConfiguration value.
        _batchSize = Math.Max(1, _configLoader.LoadCore().Pipeline.LeaseSizes.Retail);
    }

    /// <summary>
    /// Polls for <see cref="IdentityJobState.Queued"/> jobs and processes them.
    /// Called by the Api-layer hosted service on each poll tick.
    /// Returns the number of jobs processed.
    /// </summary>
    public async Task<int> PollAsync(CancellationToken ct)
    {
        var jobs = await _jobRepo.LeaseNextAsync(
            "RetailMatchWorker",
            [IdentityJobState.Queued],
            _batchSize,
            LeaseDuration,
            ct: ct);

        // Separate Music and TV jobs for group processing; everything else is per-item.
        var musicJobs = new List<IdentityJob>();
        var tvJobs    = new List<IdentityJob>();
        var otherJobs = new List<IdentityJob>();

        foreach (var job in jobs)
        {
            if (string.Equals(job.MediaType, "Music", StringComparison.OrdinalIgnoreCase))
                musicJobs.Add(job);
            else if (string.Equals(job.MediaType, "TV", StringComparison.OrdinalIgnoreCase))
                tvJobs.Add(job);
            else
                otherJobs.Add(job);
        }

        // Process non-Music/TV jobs with the existing per-item logic.
        foreach (var job in otherJobs)
        {
            try
            {
                await _concurrency.RunAsync(
                    EnrichmentWorkKind.RetailProvider,
                    token => ProcessJobAsync(job, token),
                    ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "RetailMatchWorker failed for job {JobId} (entity {EntityId})",
                    job.Id, job.EntityId);
                await _jobRepo.UpdateStateAsync(job.Id, IdentityJobState.Failed, ex.Message, ct);
            }
        }

        // Process Music jobs grouped by album (artist+album key).
        if (musicJobs.Count > 0)
        {
            await _concurrency.RunAsync(
                EnrichmentWorkKind.RetailProvider,
                token => ProcessMusicBatchAsync(musicJobs, token),
                ct);
        }

        // Process TV jobs grouped by show+season (show_name+season_number key).
        if (tvJobs.Count > 0)
        {
            await _concurrency.RunAsync(
                EnrichmentWorkKind.RetailProvider,
                token => ProcessTvBatchAsync(tvJobs, token),
                ct);
        }

        foreach (var runId in jobs
                     .Select(j => j.IngestionRunId)
                     .Where(id => id.HasValue)
                     .Select(id => id!.Value)
                     .Distinct())
        {
            await _batchProgress.EmitProgressAsync(runId, isFinal: false, ct).ConfigureAwait(false);
        }

        return jobs.Count;
    }

    // ── Music group processing ──────────────────────────────────────────────

    /// <summary>
    /// Groups Music jobs by album (artist+album) and processes each group with
    /// a single Apple album search + album track lookup instead of per-track calls.
    /// </summary>
    private async Task ProcessMusicBatchAsync(IReadOnlyList<IdentityJob> jobs, CancellationToken ct)
    {
        // Load hints for every job first (one DB call per job, in parallel would be ideal
        // but claim repo may not support concurrent reads — keep sequential to be safe).
        var jobHints = new Dictionary<Guid, Dictionary<string, string>>();
        foreach (var job in jobs)
        {
            var canonicals = await _canonicalRepo.GetByEntityAsync(job.EntityId, ct);
            var hints = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in canonicals)
                hints.TryAdd(c.Key, c.Value);
            jobHints[job.EntityId] = hints;
        }

        // Group by normalised artist+album key.
        var groups = jobs
            .GroupBy(j => BuildAlbumKey(jobHints[j.EntityId]))
            .ToList();

        _logger.LogInformation(
            "Music: grouping {TrackCount} track(s) into {GroupCount} album group(s) for retail match",
            jobs.Count, groups.Count);

        foreach (var group in groups)
        {
            var groupJobs = group.ToList();
            try
            {
                await ProcessMusicGroupAsync(groupJobs, jobHints, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex,
                    "Music: album group '{Key}' failed — falling back to per-track search for {Count} job(s)",
                    group.Key, groupJobs.Count);

                // Fall back to per-track processing for each job in this group.
                foreach (var job in groupJobs)
                {
                    try { await ProcessJobAsync(job, ct); }
                    catch (Exception innerEx) when (innerEx is not OperationCanceledException)
                    {
                        _logger.LogError(innerEx,
                            "RetailMatchWorker per-track fallback failed for {EntityId}", job.EntityId);
                        await _jobRepo.UpdateStateAsync(job.Id, IdentityJobState.Failed, innerEx.Message, ct);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Processes a group of Music jobs (all from the same album) with a track-first
    /// strategy: search Apple by the representative track to discover the correct
    /// collectionId, fetch the full album, then distribute tracks to all queued jobs.
    /// Falls back to album-name search, then per-track individual search.
    /// </summary>
    private async Task ProcessMusicGroupAsync(
        IReadOnlyList<IdentityJob> groupJobs,
        IReadOnlyDictionary<Guid, Dictionary<string, string>> jobHints,
        CancellationToken ct)
    {
        // Mark all jobs as searching.
        foreach (var job in groupJobs)
            await _jobRepo.UpdateStateAsync(job.Id, IdentityJobState.RetailSearching, ct: ct);

        var hydrationConfig = _configLoader.LoadHydration();
        var retailAcceptThreshold   = hydrationConfig.RetailAutoAcceptThreshold;
        var retailAmbiguousThreshold = hydrationConfig.RetailAmbiguousThreshold;

        var orderedGroupJobs = groupJobs
            .OrderBy(j => TryParseOrdinal(jobHints[j.EntityId].GetValueOrDefault(MetadataFieldConstants.TrackNumber), out var trackNumber)
                ? trackNumber
                : int.MaxValue)
            .ThenBy(j => jobHints[j.EntityId].GetValueOrDefault(MetadataFieldConstants.Title) ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var representativeHints = jobHints[orderedGroupJobs[0].EntityId];
        var artist = representativeHints.GetValueOrDefault(MetadataFieldConstants.Artist)
            ?? representativeHints.GetValueOrDefault(MetadataFieldConstants.Author)
            ?? representativeHints.GetValueOrDefault(MetadataFieldConstants.Composer);
        var album  = representativeHints.GetValueOrDefault(MetadataFieldConstants.Album);
        var title  = representativeHints.GetValueOrDefault(MetadataFieldConstants.Title);
        var (lang, musicCountry, _) = GetConfiguredLocale();
        var country = musicCountry;

        // ── Step 1: Track-first — search by track name to discover the collectionId.
        // A track search returns the exact track + its collectionId, so even when the
        // album name is ambiguous (remastered editions, deluxe versions), the track
        // anchors us to the correct album.
        AppleTrackSearchMatch? trackSearchMatch = null;
        string? collectionId = null;
        var resolvedVia = "track search";

        var appleProvider = _providers.FirstOrDefault(p =>
            string.Equals(p.Name, "apple_api", StringComparison.OrdinalIgnoreCase));

        if (orderedGroupJobs.Count == 1)
        {
            _logger.LogInformation(
                "Music: searching Apple iTunes by track '{Title}' / '{Artist}' ({TrackCount} queued track(s))",
                title ?? "(unknown)", artist ?? "(unknown artist)", orderedGroupJobs.Count);

            trackSearchMatch = await SearchAppleTrackAsync(artist, title, album, country, lang, ct);
            collectionId = trackSearchMatch?.CollectionId;

            if (trackSearchMatch is { SingleTrackRelease: true, TitleExact: true, ArtistExact: true })
            {
                var singleJob = orderedGroupJobs[0];
                _logger.LogInformation(
                    "Music: exact single-track Apple hit '{Title}' by '{Artist}' resolved directly via track search for entity {EntityId}",
                    title ?? "(unknown)",
                    artist ?? "(unknown artist)",
                    singleJob.EntityId);

                await ApplyMusicTrackAsync(
                    singleJob,
                    jobHints[singleJob.EntityId],
                    [trackSearchMatch.Track],
                    trackSearchMatch.CollectionId,
                    appleProvider,
                    retailAcceptThreshold,
                    retailAmbiguousThreshold,
                    ct);
                return;
            }
        }
        else
        {
            _logger.LogInformation(
                "Music: searching Apple iTunes for album '{Album}' by '{Artist}' using {TrackCount} queued track(s)",
                album ?? "(unknown album)", artist ?? "(unknown artist)", orderedGroupJobs.Count);

            var trackSearchEvidence = new List<MusicGroupTrackSearchEvidence>(orderedGroupJobs.Count);
            foreach (var job in orderedGroupJobs)
            {
                var currentHints = jobHints[job.EntityId];
                var currentTitle = currentHints.GetValueOrDefault(MetadataFieldConstants.Title);
                if (string.IsNullOrWhiteSpace(currentTitle))
                    continue;

                var match = await SearchAppleTrackAsync(artist, currentTitle, album, country, lang, ct);
                if (match is null)
                    continue;

                trackSearchEvidence.Add(new MusicGroupTrackSearchEvidence(job.EntityId, currentTitle, match));
            }

            if (trackSearchEvidence.Count > 0)
            {
                var selection = SelectBestMusicGroupCollection(trackSearchEvidence);
                collectionId = selection.CollectionId;
                resolvedVia = selection.SupportCount > 1 ? "group track consensus" : "track search";

                _logger.LogInformation(
                    "Music: selected Apple collectionId={CollectionId} for '{Artist}' / '{Album}' from {SupportCount}/{TrackCount} queued track(s) (albumExact={AlbumExactCount}, score={Score:F2})",
                    selection.CollectionId,
                    artist ?? "(unknown artist)",
                    album ?? "(unknown album)",
                    selection.SupportCount,
                    orderedGroupJobs.Count,
                    selection.AlbumExactCount,
                    selection.TotalScore);
            }
        }

        // ── Step 2: Fall back to album-name search if track search failed.
        if (collectionId is null && !string.IsNullOrWhiteSpace(album))
        {
            _logger.LogInformation(
                "Music: track search failed — falling back to album search for '{Album}' by '{Artist}'",
                album, artist ?? "(unknown)");
            collectionId = await SearchAppleAlbumAsync(artist, album, country, lang, ct);
            resolvedVia = "album search";
        }

        if (collectionId is null)
        {
            _logger.LogInformation(
                "Music: no match for '{Title}' / '{Album}' by '{Artist}' on Apple iTunes — falling back to per-track individual search for {TrackCount} job(s)",
                title ?? "(no title)", album ?? "(no album)", artist ?? "(no artist)", orderedGroupJobs.Count);

            // Last resort: process each job individually via ConfigDrivenAdapter.
            foreach (var job in orderedGroupJobs)
            {
                try { await ProcessJobAsync(job, ct); }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex,
                        "RetailMatchWorker per-track fallback failed for {EntityId}", job.EntityId);
                    await _jobRepo.UpdateStateAsync(job.Id, IdentityJobState.Failed, ex.Message, ct);
                }
            }
            return;
        }

        // ── Step 3: Fetch all tracks for the album via lookup?id={collectionId}&entity=song.
        var allTracks = await FetchAppleAlbumTracksAsync(collectionId, country, lang, ct);

        if (allTracks.Count == 0)
        {
            _logger.LogInformation(
                "RetailMatchWorker: Apple album lookup returned no tracks for collectionId={CollectionId}",
                collectionId);

            foreach (var job in orderedGroupJobs)
            {
                try { await ProcessJobAsync(job, ct); }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    await _jobRepo.UpdateStateAsync(job.Id, IdentityJobState.Failed, ex.Message, ct);
                }
            }
            return;
        }

        _logger.LogInformation(
            "Music: resolved album via {Strategy} — collectionId={CollectionId}, {TrackCount} tracks from API — distributing to {JobCount} queued track(s)",
            resolvedVia, collectionId, allTracks.Count, orderedGroupJobs.Count);

        // ── Step 4: For each job, find the best-matching track and apply its claims.
        foreach (var job in orderedGroupJobs)
        {
            var hints = jobHints[job.EntityId];
            try
            {
                await ApplyMusicTrackAsync(
                    job, hints, allTracks, collectionId,
                    appleProvider, retailAcceptThreshold, retailAmbiguousThreshold, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex,
                    "RetailMatchWorker: failed to apply track claims to job {JobId} (entity {EntityId})",
                    job.Id, job.EntityId);
                await _jobRepo.UpdateStateAsync(job.Id, IdentityJobState.Failed, ex.Message, ct);
            }
        }
    }

    /// <summary>
    /// Searches Apple iTunes by track name (entity=musicTrack) to discover the
    /// correct collectionId. Returns the collectionId from the best-matching track,
    /// or null if no match passes the threshold.
    /// </summary>
    private async Task<AppleTrackSearchMatch?> SearchAppleTrackAsync(
        string? artist, string? trackTitle, string? albumTitle, string country, string lang, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(trackTitle))
            return null;

        AppleTrackSearchMatch? bestMatch = null;

        try
        {
            using var client = _httpFactory.CreateClient("apple_api");
            foreach (var searchQuery in BuildAppleTrackSearchQueries(trackTitle, artist, albumTitle))
            {
                var query = Uri.EscapeDataString(searchQuery);
                var url = $"https://itunes.apple.com/search?term={query}&entity=musicTrack&limit=10&country={country}&lang={lang}_{country}";

                await ThrottleItunesAsync(ct);

                var json = await client.GetFromJsonAsync<JsonNode>(url, ct);
                var results = json?["results"]?.AsArray();
                if (results is null || results.Count == 0)
                    continue;

                var currentMatch = EvaluateAppleTrackSearchResults(results, artist, trackTitle, albumTitle);
                if (currentMatch is null)
                    continue;

                if (currentMatch.TitleExact && currentMatch.ArtistExact && currentMatch.SingleTrackRelease)
                {
                    var exactTrackName = currentMatch.Track["trackName"]?.GetValue<string>() ?? "(unknown)";
                    _logger.LogInformation(
                        "Music: Apple iTunes track search matched '{TrackName}' → collectionId={Id} (score={Score:F2}) for '{Artist}' / '{Title}'",
                        exactTrackName, currentMatch.CollectionId, currentMatch.Score, artist ?? "—", trackTitle);
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
                artist ?? "—", trackTitle ?? "—");
            return null;
        }

        if (bestMatch is { Score: >= 0.50 })
        {
            var bestTrackName = bestMatch.Track["trackName"]?.GetValue<string>() ?? "(unknown)";
            _logger.LogInformation(
                "Music: Apple iTunes track search matched '{TrackName}' → collectionId={Id} (score={Score:F2}) for '{Artist}' / '{Title}'",
                bestTrackName, bestMatch.CollectionId, bestMatch.Score, artist ?? "—", trackTitle);
            return bestMatch;
        }

        _logger.LogInformation(
            "Music: Apple iTunes track search — best score {Score:F2} below threshold for '{Artist}' / '{Title}'",
            bestMatch?.Score ?? 0.0, artist ?? "—", trackTitle);
        return null;
    }

    /// <summary>
    /// Searches Apple iTunes for an album entity by artist+album name.
    /// Returns the collectionId string, or null if not found.
    /// </summary>
    private async Task<string?> SearchAppleAlbumAsync(
        string? artist, string? album, string country, string lang, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(album))
            return null;

        var query = Uri.EscapeDataString($"{artist} {album}".Trim());
        var url = $"https://itunes.apple.com/search?term={query}&entity=album&limit=10&country={country}&lang={lang}_{country}";

        await ThrottleItunesAsync(ct);

        try
        {
            using var client = _httpFactory.CreateClient("apple_api");
            var json = await client.GetFromJsonAsync<JsonNode>(url, ct);

            var results = json?["results"]?.AsArray();
            if (results is null || results.Count == 0)
                return null;

            // Find best match by artist+album name overlap.
            var searchQuery = $"{artist} {album}".Trim();
            double bestScore = 0.0;
            string? bestCollectionId = null;

            foreach (var result in results)
            {
                if (result is null) continue;

                var resultCollection = result["collectionName"]?.GetValue<string>();
                var resultArtist     = result["artistName"]?.GetValue<string>();
                var resultId         = result["collectionId"]?.GetValue<long?>() is { } id
                    ? id.ToString()
                    : null;

                if (string.IsNullOrWhiteSpace(resultCollection) || resultId is null)
                    continue;

                // Score by album name similarity; artist is a secondary signal.
                var albumScore  = ComputeWordOverlap(album ?? string.Empty, resultCollection);
                var artistScore = !string.IsNullOrWhiteSpace(artist) && !string.IsNullOrWhiteSpace(resultArtist)
                    ? ComputeWordOverlap(artist, resultArtist)
                    : 0.0;

                // Combined: album name is primary (0.7), artist is secondary (0.3).
                var combined = albumScore * 0.7 + artistScore * 0.3;
                if (combined > bestScore)
                {
                    bestScore = combined;
                    bestCollectionId = resultId;
                }
            }

            // Require at least a moderate match to avoid wrong albums.
            if (bestScore >= 0.40)
            {
                _logger.LogInformation(
                    "Music: Apple iTunes album search matched collectionId={Id} (score={Score:F2}) for '{Artist}' / '{Album}'",
                    bestCollectionId, bestScore, artist ?? "—", album ?? "—");
                return bestCollectionId;
            }

            _logger.LogInformation(
                "Music: Apple iTunes album search — best score {Score:F2} below threshold for '{Artist}' / '{Album}'",
                bestScore, artist ?? "—", album ?? "—");
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "RetailMatchWorker: Apple album search failed for '{Artist}' / '{Album}'",
                artist ?? "—", album ?? "—");
            return null;
        }
    }

    /// <summary>
    /// Fetches all tracks for the given Apple collectionId via lookup?entity=song.
    /// Returns a list of raw JSON track nodes (wrapperType=track).
    /// </summary>
    private async Task<IReadOnlyList<JsonNode>> FetchAppleAlbumTracksAsync(
        string collectionId, string country, string lang, CancellationToken ct)
    {
        var url = $"https://itunes.apple.com/lookup?id={collectionId}&entity=song&country={country}&lang={lang}_{country}";

        await ThrottleItunesAsync(ct);

        try
        {
            using var client = _httpFactory.CreateClient("apple_api");
            var json = await client.GetFromJsonAsync<JsonNode>(url, ct);

            var results = json?["results"]?.AsArray();
            if (results is null || results.Count == 0)
                return [];

            // The first result is the collection itself (wrapperType=collection); skip it.
            var tracks = new List<JsonNode>();
            foreach (var node in results)
            {
                if (node is null) continue;
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

    /// <summary>
    /// Finds the best-matching track for the given job from the album track list,
    /// builds claims from that track's data, scores, and transitions the job.
    /// </summary>
    private async Task ApplyMusicTrackAsync(
        IdentityJob job,
        IReadOnlyDictionary<string, string> fileHints,
        IReadOnlyList<JsonNode> allTracks,
        string collectionId,
        IExternalMetadataProvider? appleProvider,
        double retailAcceptThreshold,
        double retailAmbiguousThreshold,
        CancellationToken ct)
    {
        var fileTitle       = fileHints.GetValueOrDefault(MetadataFieldConstants.Title);
        var fileTrackNumber = fileHints.GetValueOrDefault(MetadataFieldConstants.TrackNumber);

        var hasFileDuration = TryGetDurationSeconds(fileHints, out var fileDurationSeconds);

        // Find the best-matching track by combining title, track number, and duration.
        // Track numbers are helpful corroboration, but they should not overpower a clearly
        // wrong title on compilation albums or alternate editions.
        JsonNode? bestTrack = null;
        double bestMatchScore = -1.0;

        foreach (var track in allTracks)
        {
            var trackNumNode = track["trackNumber"]?.GetValue<long?>() is { } tn ? tn.ToString() : null;
            var trackName    = track["trackName"]?.GetValue<string>();
            var candidateHasDurationForMatch = TryGetDurationSeconds(track["trackTimeMillis"]?.GetValue<long?>(), out var candidateDurationSecondsForMatch);
            var trackNumberMatchesForMatch = !string.IsNullOrWhiteSpace(fileTrackNumber)
                && !string.IsNullOrWhiteSpace(trackNumNode)
                && string.Equals(fileTrackNumber.Trim(), trackNumNode.Trim(), StringComparison.Ordinal);
            var durationCorroboratesForMatch = hasFileDuration
                && candidateHasDurationForMatch
                && DurationsCorroborate(fileDurationSeconds, candidateDurationSecondsForMatch);

            double matchScore = !string.IsNullOrWhiteSpace(fileTitle) && !string.IsNullOrWhiteSpace(trackName)
                ? ComputeWordOverlap(fileTitle, trackName)
                : 0.0;

            if (trackNumberMatchesForMatch)
                matchScore += string.IsNullOrWhiteSpace(fileTitle) ? 0.70 : 0.25;
            else if (!string.IsNullOrWhiteSpace(fileTrackNumber) && !string.IsNullOrWhiteSpace(trackNumNode))
                matchScore -= 0.10;

            if (durationCorroboratesForMatch)
                matchScore += 0.15;

            matchScore = Math.Clamp(matchScore, 0.0, 1.0);

            if (matchScore > bestMatchScore)
            {
                bestMatchScore = matchScore;
                bestTrack = track;
            }
        }

        if (bestTrack is null || bestMatchScore < 0.30)
        {
            // No reasonable track match found — route to no-match.
            await _jobRepo.UpdateStateAsync(job.Id, IdentityJobState.RetailNoMatch, ct: ct);
            await _outcomeFactory.CreateRetailFailedAsync(
                job.EntityId, job.MediaType, job.IngestionRunId, null, ct);
            var titleHint = fileHints.GetValueOrDefault(MetadataFieldConstants.Title) ?? "(unknown)";
            await _timeline.RecordRetailNoMatchAsync(job.EntityId, titleHint, job.IngestionRunId, ct);

            _logger.LogInformation(
                "Music: no track match (score {Score:F2}) for '{Title}' in album collectionId={CollectionId} (entity {EntityId})",
                bestMatchScore, fileTitle ?? "(unknown)", collectionId, job.EntityId);
            return;
        }

        // Build claims from the matched track node.
        var claims = BuildMusicTrackClaims(bestTrack, collectionId);

        var candidateTitle  = bestTrack["trackName"]?.GetValue<string>();
        var candidateAuthor = bestTrack["artistName"]?.GetValue<string>();
        var candidateYear   = bestTrack["releaseDate"]?.GetValue<string>()?.Length >= 4
            ? bestTrack["releaseDate"]!.GetValue<string>()![..4]
            : null;
        var candidateTrackCount = bestTrack["trackCount"]?.GetValue<long?>();

        var retailScore = _retailScoring.ScoreCandidate(
            fileHints, candidateTitle, candidateAuthor, candidateYear, MediaType.Music);
        var candidateTrackNumber = bestTrack["trackNumber"]?.GetValue<long?>()?.ToString();
        var trackNumberMatches = !string.IsNullOrWhiteSpace(fileTrackNumber)
            && !string.IsNullOrWhiteSpace(candidateTrackNumber)
            && string.Equals(fileTrackNumber.Trim(), candidateTrackNumber.Trim(), StringComparison.Ordinal);
        var hasCandidateDuration = TryGetDurationSeconds(bestTrack["trackTimeMillis"]?.GetValue<long?>(), out var candidateDurationSeconds);
        var durationCorroborates = hasFileDuration
            && hasCandidateDuration
            && DurationsCorroborate(fileDurationSeconds, candidateDurationSeconds);
        var singleTrackRelease = candidateTrackCount == 1;
        var strongSingleTrackIdentity = singleTrackRelease
            && retailScore.TitleScore >= 0.95
            && retailScore.AuthorScore >= 0.85;
        var decision = EvaluateRetailDecision(
            fileHints,
            candidateTitle,
            candidateAuthor,
            candidateYear,
            retailScore,
            retailScore.CompositeScore,
            retailAcceptThreshold,
            retailAmbiguousThreshold,
            "grouped_music",
            autoAcceptCapReasons: trackNumberMatches || durationCorroborates || strongSingleTrackIdentity
                ? null
                : ["requires_track_number_or_duration_corroboration"]);

        var providerId = appleProvider?.ProviderId ?? Guid.Empty;

        var bridgeIdsJson = BuildBridgeIdsJson(claims);

        var candidate = new RetailMatchCandidate
        {
            JobId            = job.Id,
            ProviderId       = providerId,
            ProviderName     = "apple_api",
            ProviderItemId   = bestTrack["trackId"]?.GetValue<long?>()?.ToString(),
            Rank             = 1,
            Title            = candidateTitle ?? "(unknown)",
            Creator          = candidateAuthor,
            Year             = candidateYear,
            ScoreTotal       = decision.FinalScore,
            ScoreBreakdownJson = BuildScoreBreakdownJson(
                retailScore,
                decision,
                "grouped_music",
                new Dictionary<string, object?>
                {
                    ["track_match_score"] = Math.Round(bestMatchScore, 4),
                    ["track_number_matches"] = trackNumberMatches,
                    ["duration_corroborates"] = durationCorroborates,
                    ["single_track_release"] = singleTrackRelease,
                    ["strong_single_track_identity"] = strongSingleTrackIdentity,
                    ["file_duration_seconds"] = hasFileDuration ? fileDurationSeconds : null,
                    ["candidate_duration_seconds"] = hasCandidateDuration ? candidateDurationSeconds : null,
                }),
            BridgeIdsJson    = bridgeIdsJson,
            ImageUrl         = BuildAppleCoverUrl(bestTrack["artworkUrl100"]?.GetValue<string>()),
            Outcome          = decision.Outcome,
        };

        await _candidateRepo.InsertBatchAsync([candidate], ct);

        if (decision.Outcome != "Rejected")
        {
            // Phase 3c: fetch lineage so parent-scope claims (album, artist,
            // year, cover) mirror onto the album Work in addition to the track.
            WorkLineage? lineage = null;
            try { lineage = await _workRepo.GetLineageByAssetAsync(job.EntityId, ct); }
            catch (Exception ex)
            {
                _logger.LogDebug(ex,
                    "Phase 3c: lineage lookup failed for music track {EntityId} — parent mirror skipped",
                    job.EntityId);
            }

            await ScoringHelper.PersistAndScoreWithLineageAsync(
                job.EntityId, claims, providerId, lineage,
                _claimRepo, _canonicalRepo, _scoringEngine, _configLoader, _providers, ct,
                logger: _logger);

            var bridgeEntries = claims
                .Where(c => BridgeIdHelper.IsBridgeId(c.Key) && !string.IsNullOrWhiteSpace(c.Value))
                .Select(c => new BridgeIdEntry
                {
                    EntityId   = ResolveBridgeIdEntityId(lineage, job.EntityId, c.Key),
                    IdType     = c.Key,
                    IdValue    = c.Value,
                    ProviderId = providerId.ToString(),
                }).ToList();

            if (bridgeEntries.Count > 0)
                await _bridgeIdRepo.UpsertBatchAsync(bridgeEntries, ct);
        }

        if (decision.Outcome == "AutoAccepted")
        {
            await _jobRepo.SetSelectedCandidateAsync(job.Id, candidate.Id, ct);
            await _jobRepo.UpdateStateAsync(job.Id, IdentityJobState.RetailMatched, ct: ct);
            await _timeline.RecordRetailMatchedAsync(
                job.EntityId, "apple_api", 1, job.IngestionRunId, ct);

            _logger.LogInformation(
                "Music: track '{FileTitle}' → '{MatchedTitle}' from Apple iTunes album lookup (score {Score:F2}) [entity {EntityId}]",
                fileTitle ?? "(unknown)", candidateTitle, decision.FinalScore, job.EntityId);

            try
            {
                await _postPipeline.EvaluateAndOrganizeAsync(
                    job.EntityId, job.Id, wikidataQid: null, job.IngestionRunId, ct)
                    .ConfigureAwait(false);
            }
            catch (Exception orgEx) when (orgEx is not OperationCanceledException)
            {
                _logger.LogWarning(orgEx,
                    "Music: post-retail organization failed for entity {EntityId} — pipeline continues",
                    job.EntityId);
            }
        }
        else if (decision.Outcome == "Ambiguous")
        {
            await _jobRepo.SetSelectedCandidateAsync(job.Id, candidate.Id, ct);
            await _jobRepo.UpdateStateAsync(job.Id, IdentityJobState.RetailMatchedNeedsReview, ct: ct);
            await _outcomeFactory.CreateRetailAmbiguousAsync(
                job.EntityId, job.MediaType, decision.FinalScore, job.IngestionRunId, null, ct);
            await _timeline.RecordRetailMatchedAsync(
                job.EntityId, "apple_api", 1, job.IngestionRunId, ct);

            _logger.LogInformation(
                "Music: track '{FileTitle}' → '{MatchedTitle}' ambiguous on Apple iTunes (score {Score:F2}, needs review) [entity {EntityId}]",
                fileTitle ?? "(unknown)", candidateTitle, decision.FinalScore, job.EntityId);
        }
        else
        {
            await _jobRepo.UpdateStateAsync(job.Id, IdentityJobState.RetailNoMatch, ct: ct);
            await _outcomeFactory.CreateRetailFailedAsync(
                job.EntityId, job.MediaType, job.IngestionRunId, null, ct);
            var titleHint = fileHints.GetValueOrDefault(MetadataFieldConstants.Title) ?? "(unknown)";
            await _timeline.RecordRetailNoMatchAsync(job.EntityId, titleHint, job.IngestionRunId, ct);

            _logger.LogInformation(
                "Music: track '{FileTitle}' rejected — score {Score:F2} below thresholds [entity {EntityId}]",
                fileTitle ?? "(unknown)", decision.FinalScore, job.EntityId);
        }
    }

    /// <summary>
    /// Builds <see cref="ProviderClaim"/> list from a raw Apple track JSON node.
    /// Maps the same fields as the <c>apple_api</c> Music field_mappings in config.
    /// </summary>
    private static IReadOnlyList<ProviderClaim> BuildMusicTrackClaims(JsonNode track, string collectionId)
    {
        var claims = new List<ProviderClaim>();

        void Add(string key, string? value, double confidence)
        {
            if (!string.IsNullOrWhiteSpace(value))
                claims.Add(new ProviderClaim(key, value, confidence));
        }

        Add(MetadataFieldConstants.Title,              track["trackName"]?.GetValue<string>(),               0.80);
        Add(MetadataFieldConstants.Author,             track["artistName"]?.GetValue<string>(),              0.80);
        Add(MetadataFieldConstants.Artist,             track["artistName"]?.GetValue<string>(),              0.80);
        Add(MetadataFieldConstants.Album,              track["collectionName"]?.GetValue<string>(),          0.85);
        Add(MetadataFieldConstants.Genre,              track["primaryGenreName"]?.GetValue<string>(),        0.70);

        var releaseDate = track["releaseDate"]?.GetValue<string>();
        if (!string.IsNullOrWhiteSpace(releaseDate) && releaseDate.Length >= 4)
            Add(MetadataFieldConstants.Year, releaseDate[..4], 0.80);

        Add(MetadataFieldConstants.TrackNumber,
            track["trackNumber"]?.GetValue<long?>()?.ToString(), 0.90);
        Add("disc_number",
            track["discNumber"]?.GetValue<long?>()?.ToString(), 0.90);
        Add("disc_count",
            track["discCount"]?.GetValue<long?>()?.ToString(), 0.90);
        Add("track_count",
            track["trackCount"]?.GetValue<long?>()?.ToString(), 0.90);
        Add("duration",
            track["trackTimeMillis"]?.GetValue<long?>()?.ToString(), 0.90);

        // Bridge IDs.
        Add(BridgeIdKeys.AppleMusicId,
            track["trackId"]?.GetValue<long?>()?.ToString(), 0.95);
        Add(BridgeIdKeys.AppleMusicCollectionId,
            collectionId, 0.95);
        Add(BridgeIdKeys.AppleArtistId,
            track["artistId"]?.GetValue<long?>()?.ToString(), 0.90);

        // Cover art — scale up from 100px to full-res.
        var artworkUrl = BuildAppleCoverUrl(track["artworkUrl100"]?.GetValue<string>());
        if (!string.IsNullOrWhiteSpace(artworkUrl))
            claims.Add(new ProviderClaim(MetadataFieldConstants.CoverUrl, artworkUrl, 0.90));

        return claims;
    }

    /// <summary>
    /// Scales an Apple artwork URL from the 100px thumbnail to the maximum resolution.
    /// Input: <c>https://…/100x100bb.jpg</c>
    /// Output: <c>https://…/9999x9999bb.jpg</c> (Apple serves the best available size).
    /// </summary>
    private static string? BuildAppleCoverUrl(string? artworkUrl100)
    {
        if (string.IsNullOrWhiteSpace(artworkUrl100))
            return null;

        return System.Text.RegularExpressions.Regex.Replace(
            artworkUrl100,
            @"\d+x\d+bb\.jpg",
            "9999x9999bb.jpg");
    }

    // ── TV group processing ──────────────────────────────────────────────────

    /// <summary>
    /// Groups TV jobs by show+season and processes each group with a single
    /// TMDB show search + season episode list call instead of per-episode calls.
    /// </summary>
    private async Task ProcessTvBatchAsync(IReadOnlyList<IdentityJob> jobs, CancellationToken ct)
    {
        // Load hints for every job.
        var jobHints = new Dictionary<Guid, Dictionary<string, string>>();
        foreach (var job in jobs)
        {
            var canonicals = await _canonicalRepo.GetByEntityAsync(job.EntityId, ct);
            var hints = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in canonicals)
                hints.TryAdd(c.Key, c.Value);
            jobHints[job.EntityId] = hints;
        }

        // Group by show_name+season_number key.
        var groups = jobs
            .GroupBy(j => BuildShowSeasonKey(jobHints[j.EntityId]))
            .ToList();

        _logger.LogInformation(
            "TV: grouping {EpisodeCount} episode(s) into {GroupCount} show/season group(s) for retail match",
            jobs.Count, groups.Count);

        foreach (var group in groups)
        {
            var groupJobs = group.ToList();
            try
            {
                await ProcessTvGroupAsync(groupJobs, jobHints, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex,
                    "TV: show/season group '{Key}' failed — falling back to per-episode search for {Count} job(s)",
                    group.Key, groupJobs.Count);

                foreach (var job in groupJobs)
                {
                    try { await ProcessJobAsync(job, ct); }
                    catch (Exception innerEx) when (innerEx is not OperationCanceledException)
                    {
                        _logger.LogError(innerEx,
                            "RetailMatchWorker per-episode fallback failed for {EntityId}", job.EntityId);
                        await _jobRepo.UpdateStateAsync(job.Id, IdentityJobState.Failed, innerEx.Message, ct);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Processes a group of TV jobs (same show and season) with a single TMDB
    /// show search + season episode lookup. Each job receives per-episode claims.
    /// </summary>
    private async Task ProcessTvGroupAsync(
        IReadOnlyList<IdentityJob> groupJobs,
        IReadOnlyDictionary<Guid, Dictionary<string, string>> jobHints,
        CancellationToken ct)
    {
        foreach (var job in groupJobs)
            await _jobRepo.UpdateStateAsync(job.Id, IdentityJobState.RetailSearching, ct: ct);

        var hydrationConfig = _configLoader.LoadHydration();
        var retailAcceptThreshold    = hydrationConfig.RetailAutoAcceptThreshold;
        var retailAmbiguousThreshold = hydrationConfig.RetailAmbiguousThreshold;

        var providerConfigs = _configLoader.LoadAllProviders();
        var tmdbConfig = providerConfigs.FirstOrDefault(p =>
            string.Equals(p.Name, "tmdb", StringComparison.OrdinalIgnoreCase));

        var tmdbApiKey = tmdbConfig?.HttpClient?.ApiKey;
        if (string.IsNullOrWhiteSpace(tmdbApiKey))
        {
            _logger.LogWarning(
                "RetailMatchWorker: TMDB API key not configured — falling back to per-episode for {Count} jobs",
                groupJobs.Count);

            foreach (var job in groupJobs)
            {
                try { await ProcessJobAsync(job, ct); }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    await _jobRepo.UpdateStateAsync(job.Id, IdentityJobState.Failed, ex.Message, ct);
                }
            }
            return;
        }

        var representativeHints = jobHints[groupJobs[0].EntityId];
        var showName    = representativeHints.GetValueOrDefault(MetadataFieldConstants.ShowName)
            ?? representativeHints.GetValueOrDefault(MetadataFieldConstants.Series);
        var seasonStr   = representativeHints.GetValueOrDefault(MetadataFieldConstants.SeasonNumber)
            ?? representativeHints.GetValueOrDefault("season");
        // Scan ALL jobs in the group for a year claim — any episode-folder year
        // (e.g. "Shogun (2024)/Season 01/...") is enough to disambiguate the show,
        // even if the representative job's filename had no year.
        int? yearHint = null;
        foreach (var job in groupJobs)
        {
            if (!jobHints.TryGetValue(job.EntityId, out var hints)) continue;
            var candidate = hints.GetValueOrDefault(MetadataFieldConstants.Year);
            if (int.TryParse(candidate, out var parsedYear) && parsedYear > 1900)
            {
                yearHint = parsedYear;
                break;
            }
        }
        var (lang, _, country) = GetConfiguredLocale();

        // Step 1: Search TMDB for the show to get tv_id.
        _logger.LogInformation(
            "TV: searching TMDB for show '{ShowName}'{YearHint} — {EpisodeCount} episode(s) queued",
            showName ?? "(unknown)",
            yearHint.HasValue ? $" (year={yearHint.Value})" : "",
            groupJobs.Count);
        var (tvId, showPosterPath, matchedShowName) = await SearchTmdbShowAsync(showName, yearHint, tmdbApiKey, lang, country, ct);

        if (tvId is null)
        {
            _logger.LogInformation(
                "TV: no TMDB show found for '{ShowName}' — falling back to per-episode search for {EpisodeCount} job(s)",
                showName ?? "(unknown)", groupJobs.Count);

            foreach (var job in groupJobs)
            {
                try { await ProcessJobAsync(job, ct); }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    await _jobRepo.UpdateStateAsync(job.Id, IdentityJobState.Failed, ex.Message, ct);
                }
            }
            return;
        }

        var showDetails = await FetchTmdbShowDetailsAsync(tvId, tmdbApiKey, lang, country, ct);

        // Step 2: Determine unique seasons needed (may be multiple if batch spans seasons).
        var seasonGroups = groupJobs
            .GroupBy(j => jobHints[j.EntityId].GetValueOrDefault(MetadataFieldConstants.SeasonNumber)
                ?? jobHints[j.EntityId].GetValueOrDefault("season")
                ?? "1")
            .ToList();

        // Build a flat episode list across all seasons needed.
        var allEpisodes = new List<(string Season, JsonNode Node)>();
        foreach (var seasonGroup in seasonGroups)
        {
            var season = seasonGroup.Key;
            if (!int.TryParse(season, out var seasonNumber))
                seasonNumber = 1;

            var episodes = await FetchTmdbSeasonEpisodesAsync(tvId, seasonNumber, tmdbApiKey, lang, country, ct);
            foreach (var ep in episodes)
                allEpisodes.Add((season, ep));
        }

        _logger.LogInformation(
            "TV: matched show '{ShowName}' on TMDB (tv_id={TvId}), fetched {EpisodeCount} episode(s) — applying to {JobCount} queued episode(s)",
            showName ?? "—", tvId, allEpisodes.Count, groupJobs.Count);

        var tmdbProvider = _providers.FirstOrDefault(p =>
            string.Equals(p.Name, "tmdb", StringComparison.OrdinalIgnoreCase));

        // Step 3: Match each job to an episode and apply claims.
        foreach (var job in groupJobs)
        {
            var hints = jobHints[job.EntityId];
            try
            {
                await ApplyTvEpisodeAsync(
                    job, hints, allEpisodes, tvId, showPosterPath, matchedShowName, showDetails,
                    tmdbProvider, retailAcceptThreshold, retailAmbiguousThreshold, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex,
                    "RetailMatchWorker: failed to apply episode claims to job {JobId} (entity {EntityId})",
                    job.Id, job.EntityId);
                await _jobRepo.UpdateStateAsync(job.Id, IdentityJobState.Failed, ex.Message, ct);
            }
        }
    }

    /// <summary>
    /// Searches TMDB for a TV show by name. Returns the tv_id string and poster_path, or nulls if not found.
    /// When <paramref name="yearHint"/> is supplied, the search is filtered server-side by
    /// <c>first_air_date_year</c> and a year-match bonus is added during local scoring so
    /// shows with the right premiere year outrank similarly-named shows from other eras.
    /// Falls back to an unfiltered search if the year filter returns nothing.
    /// </summary>
    private async Task<(string? TvId, string? PosterPath, string? MatchedShowName)> SearchTmdbShowAsync(
        string? showName, int? yearHint, string apiKey, string lang, string country, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(showName))
            return (null, null, null);

        var query   = Uri.EscapeDataString(showName.Trim());
        var baseUrl = $"https://api.themoviedb.org/3/search/tv?query={query}&include_adult=false&language={lang}-{country}&page=1&api_key={apiKey}";
        var url     = yearHint.HasValue
            ? $"{baseUrl}&first_air_date_year={yearHint.Value}"
            : baseUrl;

        await ThrottleTmdbAsync(ct);

        try
        {
            using var client = _httpFactory.CreateClient("tmdb");
            var json = await client.GetFromJsonAsync<JsonNode>(url, ct);
            var results = json?["results"]?.AsArray();

            // If year-filtered search returned nothing, retry unfiltered (year may be wrong/missing on TMDB).
            if ((results is null || results.Count == 0) && yearHint.HasValue)
            {
                _logger.LogInformation(
                    "TV: TMDB year-filtered search returned 0 results for '{ShowName}' (year={Year}); retrying unfiltered",
                    showName, yearHint.Value);
                await ThrottleTmdbAsync(ct);
                json = await client.GetFromJsonAsync<JsonNode>(baseUrl, ct);
                results = json?["results"]?.AsArray();
            }

            if (results is null || results.Count == 0)
                return (null, null, null);

            // Pick the best match by show name similarity, biased by year proximity.
            double bestScore = 0.0;
            string? bestId = null;
            string? bestPosterPath = null;
            string? bestMatchedShowName = null;

            foreach (var result in results)
            {
                if (result is null) continue;

                var resultName = result["name"]?.GetValue<string>()
                    ?? result["original_name"]?.GetValue<string>();
                var resultId = result["id"]?.GetValue<long?>()?.ToString();

                if (string.IsNullOrWhiteSpace(resultName) || resultId is null)
                    continue;

                var nameScore = ComputeWordOverlap(showName, resultName);

                // Year bonus: +0.25 for exact match, +0.10 for ±1, -0.20 for >5 years off.
                // This is enough to outrank a slightly higher name-similarity hit from
                // the wrong era (e.g. "Shogun 2024" vs "Abarenbō Shōgun 1978").
                var yearBonus = 0.0;
                if (yearHint.HasValue)
                {
                    var firstAirDate = result["first_air_date"]?.GetValue<string>();
                    if (!string.IsNullOrWhiteSpace(firstAirDate)
                        && firstAirDate.Length >= 4
                        && int.TryParse(firstAirDate.AsSpan(0, 4), out var resultYear))
                    {
                        var diff = Math.Abs(resultYear - yearHint.Value);
                        yearBonus = diff switch
                        {
                            0     => 0.25,
                            1     => 0.10,
                            <= 5  => 0.0,
                            _     => -0.20,
                        };
                    }
                }

                var score = Math.Clamp(nameScore + yearBonus, 0.0, 1.0);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestId = resultId;
                    bestPosterPath = result["poster_path"]?.GetValue<string>();
                    bestMatchedShowName = resultName;
                }
            }

            if (bestScore >= 0.40)
            {
                _logger.LogInformation(
                    "TV: TMDB show search matched tv_id={Id} (score={Score:F2}) for '{ShowName}'{YearHint}",
                    bestId, bestScore, showName,
                    yearHint.HasValue ? $" (year={yearHint.Value})" : "");
                return (bestId, bestPosterPath, bestMatchedShowName);
            }

            _logger.LogInformation(
                "TV: TMDB show search — best score {Score:F2} below threshold for '{ShowName}'",
                bestScore, showName);
            return (null, null, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "RetailMatchWorker: TMDB show search failed for '{ShowName}'", showName);
            return (null, null, null);
        }
    }

    /// <summary>
    /// Fetches show-level TMDB details so series pages can use the provider tagline and overview.
    /// </summary>
    private async Task<JsonNode?> FetchTmdbShowDetailsAsync(
        string tvId, string apiKey, string lang, string country, CancellationToken ct)
    {
        var url = $"https://api.themoviedb.org/3/tv/{tvId}?language={lang}-{country}&api_key={apiKey}";

        await ThrottleTmdbAsync(ct);

        try
        {
            using var client = _httpFactory.CreateClient("tmdb");
            using var response = await client.GetAsync(url, ct);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return null;

            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<JsonNode>(cancellationToken: ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "RetailMatchWorker: TMDB show detail fetch failed for tv_id={TvId}", tvId);
            return null;
        }
    }

    /// <summary>
    /// Fetches all episodes for a TMDB season. Returns the raw episode JSON nodes.
    /// </summary>
    private async Task<IReadOnlyList<JsonNode>> FetchTmdbSeasonEpisodesAsync(
        string tvId, int seasonNumber, string apiKey, string lang, string country, CancellationToken ct)
    {
        var url = $"https://api.themoviedb.org/3/tv/{tvId}/season/{seasonNumber}?language={lang}-{country}&api_key={apiKey}";

        await ThrottleTmdbAsync(ct);

        try
        {
            using var client = _httpFactory.CreateClient("tmdb");
            using var response = await client.GetAsync(url, ct);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return [];

            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadFromJsonAsync<JsonNode>(cancellationToken: ct);
            var episodes = json?["episodes"]?.AsArray();
            if (episodes is null)
                return [];

            var result = new List<JsonNode>();
            foreach (var ep in episodes)
            {
                if (ep is not null)
                    result.Add(ep);
            }
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "RetailMatchWorker: TMDB season fetch failed for tv_id={TvId} season={Season}",
                tvId, seasonNumber);
            return [];
        }
    }

    /// <summary>
    /// Matches a TV job to the best episode in the season list, builds claims, scores, and transitions the job.
    /// </summary>
    private async Task ApplyTvEpisodeAsync(
        IdentityJob job,
        IReadOnlyDictionary<string, string> fileHints,
        IReadOnlyList<(string Season, JsonNode Node)> allEpisodes,
        string tvId,
        string? showPosterPath,
        string? matchedShowName,
        JsonNode? showDetails,
        IExternalMetadataProvider? tmdbProvider,
        double retailAcceptThreshold,
        double retailAmbiguousThreshold,
        CancellationToken ct)
    {
        // For TV scoring: prefer episode_title over the generic title claim.
        // VideoProcessor sets title = episode_title when available, but fileHints may
        // still carry the old show-name title for files ingested before the fix.
        // Explicitly preferring episode_title here ensures episode-vs-episode comparison.
        var fileTitle         = fileHints.GetValueOrDefault(MetadataFieldConstants.EpisodeTitle)
            ?? fileHints.GetValueOrDefault(MetadataFieldConstants.Title);
        var fileEpisodeNumber = fileHints.GetValueOrDefault(MetadataFieldConstants.EpisodeNumber)
            ?? fileHints.GetValueOrDefault("episode");
        var fileSeason        = fileHints.GetValueOrDefault(MetadataFieldConstants.SeasonNumber)
            ?? fileHints.GetValueOrDefault("season")
            ?? "1";

        // Filter to the correct season first.
        var seasonEpisodes = allEpisodes
            .Where(e => string.Equals(e.Season, fileSeason, StringComparison.OrdinalIgnoreCase))
            .Select(e => e.Node)
            .ToList();

        if (seasonEpisodes.Count == 0)
            seasonEpisodes = allEpisodes.Select(e => e.Node).ToList(); // Fallback: search all seasons.

        // Match by episode number (preferred), then by title.
        JsonNode? bestEpisode = null;
        double bestMatchScore = -1.0;

        foreach (var ep in seasonEpisodes)
        {
            var epNum   = ep["episode_number"]?.GetValue<long?>()?.ToString();
            var epTitle = ep["name"]?.GetValue<string>();

            double matchScore = 0.0;

            if (!string.IsNullOrWhiteSpace(fileEpisodeNumber) && !string.IsNullOrWhiteSpace(epNum)
                && string.Equals(fileEpisodeNumber.Trim(), epNum.Trim(), StringComparison.Ordinal))
            {
                matchScore = 1.0;
            }
            else if (!string.IsNullOrWhiteSpace(fileTitle) && !string.IsNullOrWhiteSpace(epTitle))
            {
                matchScore = ComputeWordOverlap(fileTitle, epTitle);
            }

            if (matchScore > bestMatchScore)
            {
                bestMatchScore = matchScore;
                bestEpisode = ep;
            }
        }

        if (bestEpisode is null || bestMatchScore < 0.25)
        {
            await _jobRepo.UpdateStateAsync(job.Id, IdentityJobState.RetailNoMatch, ct: ct);
            await _outcomeFactory.CreateRetailFailedAsync(
                job.EntityId, job.MediaType, job.IngestionRunId, null, ct);
            var titleHint = fileHints.GetValueOrDefault(MetadataFieldConstants.Title) ?? "(unknown)";
            await _timeline.RecordRetailNoMatchAsync(job.EntityId, titleHint, job.IngestionRunId, ct);

            _logger.LogInformation(
                "TV: no episode match (score {Score:F2}) for '{Title}' on tv_id={TvId} (entity {EntityId})",
                bestMatchScore, fileTitle ?? "(unknown)", tvId, job.EntityId);
            return;
        }

        var showName     = fileHints.GetValueOrDefault(MetadataFieldConstants.ShowName)
            ?? fileHints.GetValueOrDefault(MetadataFieldConstants.Series);
        var providerShowName = matchedShowName ?? showName;
        var showPosterUrl = BuildTmdbImageUrl(showPosterPath);
        var claims = BuildTvShowClaims(showDetails, tvId, providerShowName, showPosterUrl)
            .Concat(BuildTvEpisodeClaims(bestEpisode, tvId, providerShowName, fileSeason, showPosterUrl))
            .ToList();

        // For retail scoring, the candidate title is the episode title and author/creator
        // is the show name (best available approximation for TV scoring).
        var candidateTitle  = bestEpisode["name"]?.GetValue<string>();
        var candidateAuthor = providerShowName;
        var candidateYear   = bestEpisode["air_date"]?.GetValue<string>()?.Length >= 4
            ? bestEpisode["air_date"]!.GetValue<string>()![..4]
            : null;

        // ── Structural S/E number signal ─────────────────────────────────────
        // Season+episode number matching is a very strong structural indicator
        // that dwarfs title fuzziness (episode titles may be absent or ambiguous).
        // The bonus is computed here and passed into the scoring service so the
        // composite is produced through a single code path (no manual addition).
        var candidateEpisodeNum = bestEpisode["episode_number"]?.GetValue<long?>()?.ToString();
        var candidateSeasonNum  = bestEpisode["season_number"]?.GetValue<long?>()?.ToString()
            ?? bestEpisode["season"]?.GetValue<string>();

        bool seasonMatches  = !string.IsNullOrWhiteSpace(fileSeason)
            && !string.IsNullOrWhiteSpace(candidateSeasonNum)
            && string.Equals(fileSeason.Trim(), candidateSeasonNum.Trim(), StringComparison.Ordinal);
        bool episodeMatches = !string.IsNullOrWhiteSpace(fileEpisodeNumber)
            && !string.IsNullOrWhiteSpace(candidateEpisodeNum)
            && string.Equals(fileEpisodeNumber.Trim(), candidateEpisodeNum.Trim(), StringComparison.Ordinal);
        bool showMatches = AreEquivalentNames(showName, providerShowName);

        double structuralAdjustment = 0.0;
        if (seasonMatches && episodeMatches)
            structuralAdjustment = +0.20;   // S+E both match — very strong signal
        else if (episodeMatches && !seasonMatches)
            structuralAdjustment = +0.05;   // Episode matches but season differs — weak
        else if (!string.IsNullOrWhiteSpace(fileEpisodeNumber) && !string.IsNullOrWhiteSpace(candidateEpisodeNum))
            structuralAdjustment = -0.25;   // Episode number present but doesn't match — strong mismatch

        var retailScore = _retailScoring.ScoreCandidate(
            fileHints, candidateTitle, candidateAuthor, candidateYear, MediaType.TV,
            structuralBonus: structuralAdjustment);

        var adjustedComposite = retailScore.CompositeScore;

        // ── TV identity override ────────────────────────────────────────────
        // When we matched the show on TMDB by name AND the file's season+episode
        // exactly match a TMDB episode, the episode is uniquely identified by
        // (show_name, season, episode). The title fuzzy match contributes nothing
        // because TMDB's episode title rarely matches what the user named the
        // file (and is often missing from the file altogether). Promote to a
        // high-confidence accept so the pipeline continues to Stage 2.
        if (showMatches && seasonMatches && episodeMatches)
        {
            adjustedComposite = Math.Max(adjustedComposite, 0.90);
            _logger.LogDebug(
                "TV identity override: S{Season}E{Ep} matched on tv_id={TvId} — promoting score to {Score:F2} [entity {EntityId}]",
                fileSeason, fileEpisodeNumber, tvId, adjustedComposite, job.EntityId);
        }

        if (structuralAdjustment != 0.0)
            _logger.LogDebug(
                "TV structural adjustment: S{FileSeason}E{FileEp} vs candidate S{CandSeason}E{CandEp} → {Adj:+0.00;-0.00} (base {Base:F2} → adjusted {Adj2:F2}) [entity {EntityId}]",
                fileSeason, fileEpisodeNumber, candidateSeasonNum, candidateEpisodeNum,
                structuralAdjustment, retailScore.CompositeScore, adjustedComposite, job.EntityId);

        var decision = EvaluateRetailDecision(
            fileHints,
            candidateTitle,
            candidateAuthor,
            candidateYear,
            retailScore,
            adjustedComposite,
            retailAcceptThreshold,
            retailAmbiguousThreshold,
            "grouped_tv",
            fileCreatorOverride: showName,
            autoAcceptCapReasons: showMatches && seasonMatches && episodeMatches
                ? null
                : ["requires_exact_show_season_episode"]);

        var providerId = tmdbProvider?.ProviderId ?? Guid.Empty;

        var bridgeIdsJson = BuildBridgeIdsJson(claims);

        var candidate = new RetailMatchCandidate
        {
            JobId              = job.Id,
            ProviderId         = providerId,
            ProviderName       = "tmdb",
            ProviderItemId     = bestEpisode["id"]?.GetValue<long?>()?.ToString(),
            Rank               = 1,
            Title              = candidateTitle ?? "(unknown)",
            Creator            = candidateAuthor,
            Year               = candidateYear,
            ScoreTotal         = decision.FinalScore,
            ScoreBreakdownJson = BuildScoreBreakdownJson(
                retailScore,
                decision,
                "grouped_tv",
                new Dictionary<string, object?>
                {
                    ["show_matches"] = showMatches,
                    ["season_matches"] = seasonMatches,
                    ["episode_matches"] = episodeMatches,
                },
                structuralAdjustment),
            BridgeIdsJson      = bridgeIdsJson,
            ImageUrl           = showPosterUrl,
            Outcome            = decision.Outcome,
        };

        await _candidateRepo.InsertBatchAsync([candidate], ct);

        if (decision.Outcome != "Rejected")
        {
            // Phase 3c: fetch lineage so parent-scope claims (show_name,
            // year, description, cover) mirror onto the show Work in
            // addition to the episode.
            WorkLineage? lineage = null;
            try { lineage = await _workRepo.GetLineageByAssetAsync(job.EntityId, ct); }
            catch (Exception ex)
            {
                _logger.LogDebug(ex,
                    "Phase 3c: lineage lookup failed for TV episode {EntityId} — parent mirror skipped",
                    job.EntityId);
            }

            await ScoringHelper.PersistAndScoreWithLineageAsync(
                job.EntityId, claims, providerId, lineage,
                _claimRepo, _canonicalRepo, _scoringEngine, _configLoader, _providers, ct,
                logger: _logger);

            var bridgeEntries = claims
                .Where(c => BridgeIdHelper.IsBridgeId(c.Key) && !string.IsNullOrWhiteSpace(c.Value))
                .Select(c => new BridgeIdEntry
                {
                    EntityId   = ResolveBridgeIdEntityId(lineage, job.EntityId, c.Key),
                    IdType     = c.Key,
                    IdValue    = c.Value,
                    ProviderId = providerId.ToString(),
                }).ToList();

            if (bridgeEntries.Count > 0)
                await _bridgeIdRepo.UpsertBatchAsync(bridgeEntries, ct);

            if (lineage is not null)
            {
                await DownloadAndPersistTmdbEpisodeStillAsync(
                    bestEpisode,
                    lineage.TargetForSelfScope,
                    ct);
            }
        }

        if (decision.Outcome == "AutoAccepted")
        {
            await _jobRepo.SetSelectedCandidateAsync(job.Id, candidate.Id, ct);
            await _jobRepo.UpdateStateAsync(job.Id, IdentityJobState.RetailMatched, ct: ct);
            await _timeline.RecordRetailMatchedAsync(
                job.EntityId, "tmdb", 1, job.IngestionRunId, ct);

            _logger.LogInformation(
                "TV: '{ShowName}' S{Season}E{Episode} — '{EpisodeTitle}' matched on TMDB (score {Score:F2}) [entity {EntityId}]",
                showName ?? "(unknown)", fileSeason, fileEpisodeNumber ?? "?",
                candidateTitle, decision.FinalScore, job.EntityId);

            try
            {
                await _postPipeline.EvaluateAndOrganizeAsync(
                    job.EntityId, job.Id, wikidataQid: null, job.IngestionRunId, ct)
                    .ConfigureAwait(false);
            }
            catch (Exception orgEx) when (orgEx is not OperationCanceledException)
            {
                _logger.LogWarning(orgEx,
                    "TV: post-retail organization failed for entity {EntityId} — pipeline continues",
                    job.EntityId);
            }
        }
        else if (decision.Outcome == "Ambiguous")
        {
            await _jobRepo.SetSelectedCandidateAsync(job.Id, candidate.Id, ct);
            await _jobRepo.UpdateStateAsync(job.Id, IdentityJobState.RetailMatchedNeedsReview, ct: ct);
            await _outcomeFactory.CreateRetailAmbiguousAsync(
                job.EntityId, job.MediaType, decision.FinalScore, job.IngestionRunId, null, ct);
            await _timeline.RecordRetailMatchedAsync(
                job.EntityId, "tmdb", 1, job.IngestionRunId, ct);

            _logger.LogInformation(
                "TV: '{ShowName}' S{Season}E{Episode} — '{EpisodeTitle}' ambiguous on TMDB (score {Score:F2}, needs review) [entity {EntityId}]",
                showName ?? "(unknown)", fileSeason, fileEpisodeNumber ?? "?",
                candidateTitle, decision.FinalScore, job.EntityId);
        }
        else
        {
            await _jobRepo.UpdateStateAsync(job.Id, IdentityJobState.RetailNoMatch, ct: ct);
            await _outcomeFactory.CreateRetailFailedAsync(
                job.EntityId, job.MediaType, job.IngestionRunId, null, ct);
            var titleHint = fileHints.GetValueOrDefault(MetadataFieldConstants.Title) ?? "(unknown)";
            await _timeline.RecordRetailNoMatchAsync(job.EntityId, titleHint, job.IngestionRunId, ct);

            _logger.LogInformation(
                "TV: '{ShowName}' S{Season}E{Episode} rejected — score {Score:F2} below thresholds [entity {EntityId}]",
                showName ?? "(unknown)", fileSeason, fileEpisodeNumber ?? "?",
                decision.FinalScore, job.EntityId);
        }
    }

    private static string BuildBridgeIdsJson(IEnumerable<ProviderClaim> claims)
    {
        var bridgeIds = claims
            .Where(c => BridgeIdHelper.IsBridgeId(c.Key))
            .GroupBy(c => c.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First().Value,
                StringComparer.OrdinalIgnoreCase);

        return JsonSerializer.Serialize(bridgeIds);
    }

    /// <summary>
    /// Builds <see cref="ProviderClaim"/> list from a raw TMDB episode JSON node.
    /// Includes show-level bridge ID (tmdb_id for the show) so Stage 2 can bridge to Wikidata.
    /// </summary>
    private static IReadOnlyList<ProviderClaim> BuildTvEpisodeClaims(
        JsonNode episode, string showTvId, string? showName, string season, string? showPosterUrl = null)
    {
        var claims = new List<ProviderClaim>();

        void Add(string key, string? value, double confidence)
        {
            if (!string.IsNullOrWhiteSpace(value))
                claims.Add(new ProviderClaim(key, value, confidence));
        }

        Add(MetadataFieldConstants.EpisodeTitle, episode["name"]?.GetValue<string>(), 0.85);
        Add(MetadataFieldConstants.Cover, showPosterUrl, 0.90);

        // For TV, "title" in the system is typically the episode title.
        Add(MetadataFieldConstants.Title,         episode["name"]?.GetValue<string>(), 0.80);
        Add(MetadataFieldConstants.EpisodeDescription, episode["overview"]?.GetValue<string>(), 0.85);
        Add(MetadataFieldConstants.ShowName,      showName, 0.85);

        var airDate = episode["air_date"]?.GetValue<string>();
        if (!string.IsNullOrWhiteSpace(airDate) && airDate.Length >= 4)
            Add(MetadataFieldConstants.Year, airDate[..4], 0.85);

        Add(MetadataFieldConstants.SeasonNumber,
            episode["season_number"]?.GetValue<long?>()?.ToString() ?? season, 0.90);
        Add(MetadataFieldConstants.EpisodeNumber,
            episode["episode_number"]?.GetValue<long?>()?.ToString(), 0.90);

        // The show-level TMDB ID is the critical bridge ID for Stage 2 Wikidata resolution.
        // Episode-level TMDB IDs are available but the show QID is what Wikidata resolves.
        Add(BridgeIdKeys.TmdbId, showTvId, 1.0);

        var rating = episode["vote_average"]?.GetValue<double?>()?.ToString("F1");
        if (!string.IsNullOrWhiteSpace(rating))
            Add(MetadataFieldConstants.Rating, rating, 0.80);

        return claims;
    }

    private static IReadOnlyList<ProviderClaim> BuildTvShowClaims(
        JsonNode? showDetails, string showTvId, string? fallbackShowName, string? fallbackPosterUrl)
    {
        var claims = new List<ProviderClaim>();

        void Add(string key, string? value, double confidence)
        {
            if (!string.IsNullOrWhiteSpace(value))
                claims.Add(new ProviderClaim(key, value, confidence));
        }

        Add(MetadataFieldConstants.ShowName, showDetails?["name"]?.GetValue<string>() ?? fallbackShowName, 0.90);
        Add(MetadataFieldConstants.Title, showDetails?["name"]?.GetValue<string>() ?? fallbackShowName, 0.86);
        Add(MetadataFieldConstants.Description, showDetails?["overview"]?.GetValue<string>(), 0.86);
        Add(MetadataFieldConstants.Tagline, showDetails?["tagline"]?.GetValue<string>(), 0.78);
        Add(MetadataFieldConstants.Network, showDetails?["networks"]?[0]?["name"]?.GetValue<string>(), 0.85);
        Add(MetadataFieldConstants.Cover, BuildTmdbImageUrl(showDetails?["poster_path"]?.GetValue<string>()) ?? fallbackPosterUrl, 0.90);
        Add(BridgeIdKeys.TmdbId, showTvId, 1.0);

        var firstAirDate = showDetails?["first_air_date"]?.GetValue<string>();
        if (!string.IsNullOrWhiteSpace(firstAirDate) && firstAirDate.Length >= 4)
            Add(MetadataFieldConstants.Year, firstAirDate[..4], 0.85);

        return claims;
    }

    /// <summary>
    /// Builds a TMDB still image URL from a still_path value.
    /// </summary>
    private static string? BuildTmdbImageUrl(string? stillPath)
    {
        if (string.IsNullOrWhiteSpace(stillPath))
            return null;

        return $"https://image.tmdb.org/t/p/w500{stillPath}";
    }

    private async Task DownloadAndPersistTmdbEpisodeStillAsync(
        JsonNode episode,
        Guid episodeWorkId,
        CancellationToken ct)
    {
        if (_entityAssetRepo is null || _assetPaths is null)
            return;

        var stillUrl = BuildTmdbImageUrl(episode["still_path"]?.GetValue<string>());
        if (string.IsNullOrWhiteSpace(stillUrl))
            return;

        var existingVariants = (await _entityAssetRepo.GetByEntityAsync(
            episodeWorkId.ToString(),
            AssetType.EpisodeStill.ToString(),
            ct)).ToList();

        var userOverride = existingVariants.FirstOrDefault(asset => asset.IsPreferred && asset.IsUserOverride);
        if (userOverride is not null)
        {
            _logger.LogDebug(
                "TV: preserving user-selected episode still for Work {EpisodeWorkId}",
                episodeWorkId);
            return;
        }

        var existingTmdbStill = existingVariants.FirstOrDefault(asset =>
            string.Equals(asset.SourceProvider, "tmdb", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(asset.LocalImagePath)
            && File.Exists(asset.LocalImagePath));

        if (existingTmdbStill is not null)
        {
            await _entityAssetRepo.SetPreferredAsync(existingTmdbStill.Id, ct);
            await UpsertPreferredEpisodeStillCanonicalAsync(episodeWorkId, existingTmdbStill, ct);
            return;
        }

        byte[] bytes;
        try
        {
            using var client = _httpFactory.CreateClient("tmdb");
            bytes = await client.GetByteArrayAsync(stillUrl, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex,
                "TV: failed to download TMDB episode still for Work {EpisodeWorkId}",
                episodeWorkId);
            return;
        }

        if (bytes.Length == 0)
            return;

        var variant = new EntityAsset
        {
            Id = Guid.NewGuid(),
            EntityId = episodeWorkId.ToString(),
            EntityType = "Work",
            AssetTypeValue = AssetType.EpisodeStill.ToString(),
            ImageUrl = null,
            LocalImagePath = string.Empty,
            SourceProvider = "tmdb",
            AssetClassValue = "Artwork",
            StorageLocationValue = "Central",
            OwnerScope = "Episode",
            IsPreferred = false,
            IsUserOverride = false,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        variant.LocalImagePath = _assetPaths.GetCentralAssetPath(
            "Work",
            episodeWorkId,
            AssetType.EpisodeStill.ToString(),
            variant.Id,
            InferTmdbStillExtension(stillUrl));

        await PersistTmdbEpisodeStillBytesAsync(bytes, variant.LocalImagePath, stillUrl, ct);
        ArtworkVariantHelper.StampMetadataAndRenditions(variant, _assetPaths);
        await _entityAssetRepo.UpsertAsync(variant, ct);
        await _entityAssetRepo.SetPreferredAsync(variant.Id, ct);
        await UpsertPreferredEpisodeStillCanonicalAsync(episodeWorkId, variant, ct);

        if (_assetExportService is not null)
            await _assetExportService.ReconcileArtworkAsync(
                variant.EntityId,
                variant.EntityType,
                variant.AssetTypeValue,
                ct);

        _logger.LogInformation(
            "TV: downloaded TMDB episode still for Work {EpisodeWorkId} ({Bytes} bytes)",
            episodeWorkId,
            bytes.Length);
    }

    private async Task PersistTmdbEpisodeStillBytesAsync(
        byte[] bytes,
        string destinationPath,
        string sourceUrl,
        CancellationToken ct)
    {
        AssetPathService.EnsureDirectory(destinationPath);

        if (_imageCache is null)
        {
            await File.WriteAllBytesAsync(destinationPath, bytes, ct);
            return;
        }

        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var cachedPath = await _imageCache.FindByHashAsync(hash, ct);
        if (!string.IsNullOrWhiteSpace(cachedPath) && File.Exists(cachedPath))
        {
            if (!string.Equals(cachedPath, destinationPath, StringComparison.OrdinalIgnoreCase))
                File.Copy(cachedPath, destinationPath, overwrite: true);

            return;
        }

        await File.WriteAllBytesAsync(destinationPath, bytes, ct);
        await _imageCache.InsertAsync(hash, destinationPath, sourceUrl, ct);
    }

    private async Task UpsertPreferredEpisodeStillCanonicalAsync(
        Guid episodeWorkId,
        EntityAsset preferredVariant,
        CancellationToken ct)
    {
        await _canonicalRepo.UpsertBatchAsync(
            ArtworkCanonicalHelper.CreatePreferredAssetCanonicals(
                episodeWorkId,
                preferredVariant,
                DateTimeOffset.UtcNow),
            ct);
    }

    private static string InferTmdbStillExtension(string imageUrl)
    {
        if (Uri.TryCreate(imageUrl, UriKind.Absolute, out var imageUri))
        {
            var extension = Path.GetExtension(imageUri.AbsolutePath);
            if (string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase))
                return ".png";
        }

        return ".jpg";
    }

    // ── Grouping key helpers ─────────────────────────────────────────────────

    private static string BuildAlbumKey(Dictionary<string, string> hints)
    {
        hints.TryGetValue(MetadataFieldConstants.Artist, out var artist);
        if (string.IsNullOrWhiteSpace(artist))
            hints.TryGetValue(MetadataFieldConstants.Author, out artist);
        if (string.IsNullOrWhiteSpace(artist))
            hints.TryGetValue(MetadataFieldConstants.Composer, out artist);

        hints.TryGetValue(MetadataFieldConstants.Album, out var album);

        // Normalise: lowercase, trim — so "The Beatles" and "the beatles" group together.
        return $"{(artist ?? string.Empty).Trim().ToLowerInvariant()}|{(album ?? string.Empty).Trim().ToLowerInvariant()}";
    }

    private static string BuildShowSeasonKey(Dictionary<string, string> hints)
    {
        hints.TryGetValue(MetadataFieldConstants.ShowName, out var showName);
        if (string.IsNullOrWhiteSpace(showName))
            hints.TryGetValue(MetadataFieldConstants.Series, out showName);

        hints.TryGetValue(MetadataFieldConstants.SeasonNumber, out var season);
        if (string.IsNullOrWhiteSpace(season))
            hints.TryGetValue("season", out season);

        return $"{(showName ?? string.Empty).Trim().ToLowerInvariant()}|{(season ?? "1").Trim()}";
    }

    // ── Throttle helpers ─────────────────────────────────────────────────────

    private async Task ThrottleItunesAsync(CancellationToken ct)
    {
        await _itunesThrottle.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            const int throttleMs = 300;
            var elapsed = (DateTime.UtcNow - _itunesLastCallUtc).TotalMilliseconds;
            if (elapsed < throttleMs)
                await Task.Delay(TimeSpan.FromMilliseconds(throttleMs - elapsed), ct)
                    .ConfigureAwait(false);
            _itunesLastCallUtc = DateTime.UtcNow;
        }
        finally
        {
            _itunesThrottle.Release();
        }
    }

    private async Task ThrottleTmdbAsync(CancellationToken ct)
    {
        await _tmdbThrottle.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            const int throttleMs = 250;
            var elapsed = (DateTime.UtcNow - _tmdbLastCallUtc).TotalMilliseconds;
            if (elapsed < throttleMs)
                await Task.Delay(TimeSpan.FromMilliseconds(throttleMs - elapsed), ct)
                    .ConfigureAwait(false);
            _tmdbLastCallUtc = DateTime.UtcNow;
        }
        finally
        {
            _tmdbThrottle.Release();
        }
    }

    private (string Language, string MusicCountry, string RegionCountry) GetConfiguredLocale()
    {
        var core = _configLoader.LoadCore();
        var rawLanguage = string.IsNullOrWhiteSpace(core.Language.Metadata)
            ? "en"
            : core.Language.Metadata.Trim();
        var language = rawLanguage
            .Split(['-', '_'], StringSplitOptions.RemoveEmptyEntries)[0]
            .ToLowerInvariant();
        var regionCountry = string.IsNullOrWhiteSpace(core.Country)
            ? "US"
            : core.Country.Trim().ToUpperInvariant();
        return (language, regionCountry.ToLowerInvariant(), regionCountry);
    }

    // ── Word overlap similarity (mirror of ConfigDrivenAdapter) ───────────────

    /// <summary>
    /// F1 word-overlap similarity (0.0–1.0). Strips diacritics and normalises to lowercase.
    /// </summary>
    private static double ComputeWordOverlap(string a, string b)
    {
        var aWords = Tokenize(a);
        var bWords = Tokenize(b);

        if (aWords.Count == 0 || bWords.Count == 0)
            return 0.0;

        var coverage  = (double)aWords.Count(w => bWords.Contains(w)) / aWords.Count;
        var precision = (double)bWords.Count(w => aWords.Contains(w)) / bWords.Count;

        if (coverage + precision == 0) return 0.0;
        return 2 * coverage * precision / (coverage + precision);
    }

    private static HashSet<string> Tokenize(string text)
    {
        return [.. StripDiacritics(text).ToLowerInvariant()
            .Split([' ', ',', '.', '-', ':', ';', '\'', '"', '(', ')', '[', ']'],
                   StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length >= 2)];
    }

    /// <summary>
    /// Strips Unicode combining marks so "Shōgun" matches "Shogun" during
    /// name comparison. Used by Tokenize to avoid losing matches to diacritics
    /// in TMDB localized titles.
    /// </summary>
    private static string StripDiacritics(string text)
    {
        var normalized = text.Normalize(System.Text.NormalizationForm.FormD);
        var sb = new System.Text.StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c)
                != System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                sb.Append(c);
            }
        }
        return sb.ToString().Normalize(System.Text.NormalizationForm.FormC);
    }

    // ── Per-item fallback (Books, Audiobooks, Movies, Comics) ──────

    private static string? GetPrimaryCreatorHint(IReadOnlyDictionary<string, string> fileHints)
    {
        return fileHints.GetValueOrDefault(MetadataFieldConstants.Author)
            ?? fileHints.GetValueOrDefault(MetadataFieldConstants.Artist)
            ?? fileHints.GetValueOrDefault(MetadataFieldConstants.Composer)
            ?? fileHints.GetValueOrDefault(MetadataFieldConstants.Director)
            ?? fileHints.GetValueOrDefault("writer")
            ?? fileHints.GetValueOrDefault(MetadataFieldConstants.ShowName)
            ?? fileHints.GetValueOrDefault(MetadataFieldConstants.Series);
    }

    private static string? GetPrimaryYearHint(IReadOnlyDictionary<string, string> fileHints)
    {
        return NormalizeYearValue(
            fileHints.GetValueOrDefault(MetadataFieldConstants.Year)
            ?? fileHints.GetValueOrDefault("release_year")
            ?? fileHints.GetValueOrDefault("date")
            ?? fileHints.GetValueOrDefault("release_date"));
    }

    private static string? NormalizeYearValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var match = System.Text.RegularExpressions.Regex.Match(value, @"\b\d{4}\b");
        return match.Success ? match.Value : null;
    }

    private static (double StructuralBonus, Dictionary<string, object?> Evidence) ComputeSingleItemStructuralSignal(
        MediaType mediaType,
        IReadOnlyDictionary<string, string> fileHints,
        IReadOnlyList<ProviderClaim> claims)
    {
        var evidence = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        double structuralBonus = 0.0;

        var exactBridgeMatches = claims
            .Where(c => BridgeIdHelper.IsBridgeId(c.Key) && !string.IsNullOrWhiteSpace(c.Value))
            .Count(c => fileHints.TryGetValue(c.Key, out var fileValue)
                && string.Equals(fileValue?.Trim(), c.Value.Trim(), StringComparison.OrdinalIgnoreCase));

        if (exactBridgeMatches > 0)
        {
            structuralBonus += 0.35;
            evidence["exact_bridge_id_matches"] = exactBridgeMatches;
        }

        if (mediaType == MediaType.Comics)
        {
            var fileTitle = fileHints.GetValueOrDefault(MetadataFieldConstants.Title);
            var candidateTitle = claims
                .FirstOrDefault(c => string.Equals(c.Key, MetadataFieldConstants.Title, StringComparison.OrdinalIgnoreCase))
                ?.Value;
            var fileSeries = fileHints.GetValueOrDefault(MetadataFieldConstants.Series);
            var candidateSeries = claims
                .FirstOrDefault(c => string.Equals(c.Key, MetadataFieldConstants.Series, StringComparison.OrdinalIgnoreCase))
                ?.Value;
            var fileIssue = fileHints.GetValueOrDefault(MetadataFieldConstants.SeriesPosition)
                ?? fileHints.GetValueOrDefault("issue_number");
            var candidateIssue = claims
                .FirstOrDefault(c => string.Equals(c.Key, MetadataFieldConstants.SeriesPosition, StringComparison.OrdinalIgnoreCase))
                ?.Value
                ?? claims.FirstOrDefault(c => string.Equals(c.Key, "issue_number", StringComparison.OrdinalIgnoreCase))
                    ?.Value;

            var seriesMatches = AreEquivalentNames(fileSeries, candidateSeries);
            var issueMatches = AreEquivalentOrdinals(fileIssue, candidateIssue);
            var titleMatches = AreEquivalentNames(fileTitle, candidateTitle);
            var fileTitleContainsFileSeries = TitleContainsSeriesAnchor(fileTitle, fileSeries);
            var fileTitleContainsCandidateSeries = TitleContainsSeriesAnchor(fileTitle, candidateSeries);
            var candidateTitleContainsFileSeries = TitleContainsSeriesAnchor(candidateTitle, fileSeries);
            var titleAnchorsIssueIdentity = titleMatches
                && (seriesMatches
                    || fileTitleContainsFileSeries
                    || fileTitleContainsCandidateSeries
                    || candidateTitleContainsFileSeries);

            evidence["series_matches"] = seriesMatches;
            evidence["issue_matches"] = issueMatches;
            evidence["title_matches"] = titleMatches;
            evidence["file_title_contains_file_series"] = fileTitleContainsFileSeries;
            evidence["file_title_contains_candidate_series"] = fileTitleContainsCandidateSeries;
            evidence["candidate_title_contains_file_series"] = candidateTitleContainsFileSeries;
            evidence["title_anchors_issue_identity"] = titleAnchorsIssueIdentity;

            if (seriesMatches && issueMatches)
                structuralBonus += 0.35;
            else if (titleAnchorsIssueIdentity)
                structuralBonus += 0.35;
            else if (issueMatches)
                structuralBonus += 0.20;

            var applyIssueMismatchPenalty = !titleAnchorsIssueIdentity
                && !string.IsNullOrWhiteSpace(fileIssue)
                && !string.IsNullOrWhiteSpace(candidateIssue)
                && !issueMatches;
            evidence["issue_mismatch_penalty_applied"] = applyIssueMismatchPenalty;

            if (applyIssueMismatchPenalty)
                structuralBonus -= 0.25;
        }

        return (structuralBonus, evidence);
    }

    private static bool AreEquivalentOrdinals(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            return false;

        if (int.TryParse(left, out var leftNumber) && int.TryParse(right, out var rightNumber))
            return leftNumber == rightNumber;

        return string.Equals(left.TrimStart('0'), right.TrimStart('0'), StringComparison.OrdinalIgnoreCase)
            || string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeComparableText(string text)
    {
        var chars = StripDiacritics(text)
            .Replace("&", " and ", StringComparison.Ordinal)
            .ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : ' ')
            .ToArray();

        return string.Join(' ', new string(chars)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static bool AreEquivalentNames(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            return false;

        return string.Equals(
            NormalizeComparableText(left),
            NormalizeComparableText(right),
            StringComparison.Ordinal);
    }

    private static bool TitleContainsSeriesAnchor(string? title, string? series)
    {
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(series))
            return false;

        var normalizedTitle = NormalizeComparableText(title);
        var normalizedSeries = NormalizeComparableText(series);
        if (string.IsNullOrWhiteSpace(normalizedTitle) || string.IsNullOrWhiteSpace(normalizedSeries))
            return false;

        return normalizedTitle.Contains(normalizedSeries, StringComparison.Ordinal);
    }

    private static bool TryGetDurationSeconds(IReadOnlyDictionary<string, string> fileHints, out double seconds)
    {
        if (TryGetNumericSeconds(fileHints.GetValueOrDefault("duration_sec"), false, out seconds))
            return true;

        return TryParseFlexibleDuration(fileHints.GetValueOrDefault(MetadataFieldConstants.DurationField), out seconds);
    }

    private static bool TryGetDurationSeconds(long? milliseconds, out double seconds)
    {
        seconds = 0.0;
        if (milliseconds is not > 0)
            return false;

        seconds = milliseconds.Value / 1000.0;
        return true;
    }

    private static bool TryParseFlexibleDuration(string? value, out double seconds)
    {
        if (TryGetNumericSeconds(value, true, out seconds))
            return true;

        if (!string.IsNullOrWhiteSpace(value)
            && TimeSpan.TryParse(value, out var timeSpan)
            && timeSpan.TotalSeconds > 0)
        {
            seconds = timeSpan.TotalSeconds;
            return true;
        }

        seconds = 0.0;
        return false;
    }

    private static bool TryParseOrdinal(string? value, out int ordinal)
    {
        ordinal = 0;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var digits = new string(value.Where(char.IsDigit).ToArray());
        return !string.IsNullOrWhiteSpace(digits)
            && int.TryParse(digits, out ordinal)
            && ordinal > 0;
    }

    private sealed record AppleTrackSearchMatch(
        string CollectionId,
        JsonNode Track,
        double Score,
        bool TitleExact,
        bool ArtistExact,
        bool SingleTrackRelease,
        bool AlbumExact,
        double AlbumScore);

    private sealed record MusicGroupTrackSearchEvidence(
        Guid EntityId,
        string Title,
        AppleTrackSearchMatch Match);

    private sealed record MusicGroupCollectionSelection(
        string CollectionId,
        int SupportCount,
        int AlbumExactCount,
        double TotalAlbumScore,
        double TotalScore);

    private static IReadOnlyList<string> BuildAppleTrackSearchQueries(
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

    private static AppleTrackSearchMatch? EvaluateAppleTrackSearchResults(
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
            if (result is null) continue;

            var resultTrackName = result["trackName"]?.GetValue<string>();
            var resultArtist = result["artistName"]?.GetValue<string>();
            var resultAlbum = result["collectionName"]?.GetValue<string>();
            var resultTrackCount = result["trackCount"]?.GetValue<long?>();
            var resultId = result["collectionId"]?.GetValue<long?>() is { } id
                ? id.ToString()
                : null;

            if (string.IsNullOrWhiteSpace(resultTrackName) || resultId is null)
                continue;

            var titleScore = ComputeWordOverlap(trackTitle, resultTrackName);
            var artistScore = !string.IsNullOrWhiteSpace(artist) && !string.IsNullOrWhiteSpace(resultArtist)
                ? ComputeWordOverlap(artist, resultArtist)
                : 0.0;
            var albumScore = !string.IsNullOrWhiteSpace(albumTitle) && !string.IsNullOrWhiteSpace(resultAlbum)
                ? ComputeWordOverlap(albumTitle, resultAlbum)
                : 0.0;
            var titleExact = AreEquivalentNames(trackTitle, resultTrackName);
            var artistExact = AreEquivalentNames(artist, resultArtist);
            var albumExact = AreEquivalentNames(albumTitle, resultAlbum);
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

    private static MusicGroupCollectionSelection SelectBestMusicGroupCollection(
        IReadOnlyList<MusicGroupTrackSearchEvidence> evidence)
    {
        return evidence
            .GroupBy(e => e.Match.CollectionId, StringComparer.Ordinal)
            .Select(group => new MusicGroupCollectionSelection(
                CollectionId: group.Key,
                SupportCount: group.Count(),
                AlbumExactCount: group.Count(e => e.Match.AlbumExact),
                TotalAlbumScore: Math.Round(group.Sum(e => e.Match.AlbumScore), 4),
                TotalScore: Math.Round(group.Sum(e => e.Match.Score), 4)))
            .OrderByDescending(candidate => candidate.SupportCount)
            .ThenByDescending(candidate => candidate.AlbumExactCount)
            .ThenByDescending(candidate => candidate.TotalAlbumScore)
            .ThenByDescending(candidate => candidate.TotalScore)
            .First();
    }

    private static bool TryGetNumericSeconds(string? value, bool preferMillisecondsForLargeValues, out double seconds)
    {
        seconds = 0.0;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        if (!double.TryParse(value, out var raw) || raw <= 0)
            return false;

        seconds = preferMillisecondsForLargeValues && raw > 20000
            ? raw / 1000.0
            : raw;

        return seconds > 0;
    }

    private static bool DurationsCorroborate(double fileDurationSeconds, double candidateDurationSeconds)
    {
        if (fileDurationSeconds <= 0 || candidateDurationSeconds <= 0)
            return false;

        var absoluteDiff = Math.Abs(fileDurationSeconds - candidateDurationSeconds);
        var relativeDiff = absoluteDiff / Math.Max(fileDurationSeconds, candidateDurationSeconds);
        return absoluteDiff <= 5 || relativeDiff <= 0.15;
    }

    private static double ComputeTextEvidence(
        FieldMatchScores score,
        bool creatorPresentOnBothSides,
        bool yearPresentOnBothSides)
    {
        const double titleWeight = 0.60;
        const double creatorWeight = 0.25;
        const double yearWeight = 0.15;

        var weightedScore = score.TitleScore * titleWeight;
        var totalWeight = titleWeight;

        if (creatorPresentOnBothSides)
        {
            weightedScore += score.AuthorScore * creatorWeight;
            totalWeight += creatorWeight;
        }

        if (yearPresentOnBothSides)
        {
            weightedScore += score.YearScore * yearWeight;
            totalWeight += yearWeight;
        }

        return totalWeight <= 0
            ? 0.0
            : Math.Round(weightedScore / totalWeight, 4);
    }

    private static RetailDecision EvaluateRetailDecision(
        IReadOnlyDictionary<string, string> fileHints,
        string? candidateTitle,
        string? candidateCreator,
        string? candidateYear,
        FieldMatchScores retailScore,
        double finalScore,
        double retailAcceptThreshold,
        double retailAmbiguousThreshold,
        string matchContext,
        string? fileCreatorOverride = null,
        IReadOnlyList<string>? autoAcceptCapReasons = null)
    {
        const double weakCreatorThreshold = 0.55;
        const double weakTextThreshold = 0.60;
        const double weakTitleThreshold = 0.50;

        _ = candidateTitle;

        var fileCreator = fileCreatorOverride ?? GetPrimaryCreatorHint(fileHints);
        var fileYear = GetPrimaryYearHint(fileHints);
        candidateYear = NormalizeYearValue(candidateYear);

        var creatorPresentOnBothSides = !string.IsNullOrWhiteSpace(fileCreator)
            && !string.IsNullOrWhiteSpace(candidateCreator);
        var yearPresentOnBothSides = !string.IsNullOrWhiteSpace(fileYear)
            && !string.IsNullOrWhiteSpace(candidateYear);
        var creatorDirectMatch = creatorPresentOnBothSides
            && AreEquivalentNames(fileCreator, candidateCreator);
        var creatorContradiction = creatorPresentOnBothSides
            && !creatorDirectMatch
            && retailScore.AuthorScore < weakCreatorThreshold;
        var textEvidence = ComputeTextEvidence(
            retailScore,
            creatorPresentOnBothSides,
            yearPresentOnBothSides);
        var weakTextEvidence = retailScore.TitleScore < weakTitleThreshold
            || textEvidence < weakTextThreshold;

        var scoreWithoutCover = Math.Max(0.0, finalScore - retailScore.CoverArtScore);
        var coverWouldRescueWeakText = retailScore.CoverArtScore > 0.0
            && weakTextEvidence
            && finalScore >= retailAmbiguousThreshold
            && scoreWithoutCover < retailAmbiguousThreshold;

        var rejectionReasons = new List<string>();

        string outcome;
        string thresholdPath;
        if (finalScore >= retailAcceptThreshold)
        {
            outcome = "AutoAccepted";
            thresholdPath = "accept_threshold";
        }
        else if (finalScore >= retailAmbiguousThreshold)
        {
            outcome = "Ambiguous";
            thresholdPath = "ambiguous_threshold";
        }
        else
        {
            outcome = "Rejected";
            thresholdPath = "below_ambiguous_threshold";
        }

        var autoAcceptBlocked = false;

        if (creatorContradiction)
        {
            rejectionReasons.Add("creator_similarity_weak");
            if (outcome == "AutoAccepted")
            {
                outcome = "Ambiguous";
                thresholdPath = "accept_capped_to_review";
                autoAcceptBlocked = true;
            }
        }

        if (autoAcceptCapReasons is { Count: > 0 })
        {
            foreach (var reason in autoAcceptCapReasons.Where(r => !string.IsNullOrWhiteSpace(r)))
            {
                if (!rejectionReasons.Contains(reason, StringComparer.Ordinal))
                    rejectionReasons.Add(reason);
            }

            if (outcome == "AutoAccepted")
            {
                outcome = "Ambiguous";
                thresholdPath = "accept_capped_to_review";
                autoAcceptBlocked = true;
            }
        }

        if (coverWouldRescueWeakText)
        {
            if (!rejectionReasons.Contains("cover_cannot_rescue_weak_text", StringComparer.Ordinal))
                rejectionReasons.Add("cover_cannot_rescue_weak_text");

            outcome = "Rejected";
            thresholdPath = "cover_rescue_rejected";
            autoAcceptBlocked = true;
        }

        return new RetailDecision(
            Outcome: outcome,
            FinalScore: Math.Round(finalScore, 4),
            ThresholdPath: thresholdPath,
            RejectionReasons: rejectionReasons,
            TextEvidence: textEvidence,
            CreatorPresentOnBothSides: creatorPresentOnBothSides,
            CreatorContradiction: creatorContradiction,
            AutoAcceptBlocked: autoAcceptBlocked,
            MatchContext: matchContext);
    }

    private static string BuildScoreBreakdownJson(
        FieldMatchScores retailScore,
        RetailDecision decision,
        string matchContext,
        IReadOnlyDictionary<string, object?>? extraEvidence = null,
        double structuralBonus = 0.0)
    {
        var breakdown = new Dictionary<string, object?>
        {
            ["title"] = retailScore.TitleScore,
            ["author"] = retailScore.AuthorScore,
            ["year"] = retailScore.YearScore,
            ["format"] = retailScore.FormatScore,
            ["cross_field"] = retailScore.CrossFieldBoost,
            ["cover"] = retailScore.CoverArtScore,
            ["final_score"] = decision.FinalScore,
            ["text_evidence"] = decision.TextEvidence,
            ["threshold_path"] = decision.ThresholdPath,
            ["rejection_reasons"] = decision.RejectionReasons,
            ["match_context"] = matchContext,
            ["creator_present_on_both_sides"] = decision.CreatorPresentOnBothSides,
            ["creator_contradiction"] = decision.CreatorContradiction,
            ["auto_accept_blocked"] = decision.AutoAcceptBlocked,
        };

        if (structuralBonus != 0.0)
            breakdown["structural_bonus"] = structuralBonus;

        if (extraEvidence is not null)
        {
            foreach (var pair in extraEvidence)
                breakdown[pair.Key] = pair.Value;
        }

        return JsonSerializer.Serialize(breakdown);
    }

    private static int GetOutcomeRank(string outcome) => outcome switch
    {
        "AutoAccepted" => 2,
        "Ambiguous" => 1,
        _ => 0,
    };

    private static bool IsBetterCandidate(RetailMatchCandidate candidate, RetailMatchCandidate? currentBest)
    {
        if (currentBest is null)
            return true;

        var candidateRank = GetOutcomeRank(candidate.Outcome);
        var bestRank = GetOutcomeRank(currentBest.Outcome);
        if (candidateRank != bestRank)
            return candidateRank > bestRank;

        if (Math.Abs(candidate.ScoreTotal - currentBest.ScoreTotal) > 0.0001)
            return candidate.ScoreTotal > currentBest.ScoreTotal;

        return candidate.Rank < currentBest.Rank;
    }

    internal async Task ProcessJobAsync(IdentityJob job, CancellationToken ct)
    {
        await _jobRepo.UpdateStateAsync(job.Id, IdentityJobState.RetailSearching, ct: ct);

        if (!Enum.TryParse<MediaType>(job.MediaType, true, out var mediaType))
            mediaType = MediaType.Unknown;

        // Look up the asset's work lineage once. Used by the router below to
        // write provider bridge IDs to the correct Work — track-level IDs
        // (apple_music_id, isrc) on the asset's own Work; album-level IDs
        // (apple_music_collection_id, musicbrainz_id) on the parent. Null
        // when the job targets a Work directly (manual flows) — in that case
        // we skip work-level routing entirely.
        WorkLineage? lineage = null;
        if (string.Equals(job.EntityType, "MediaAsset", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                lineage = await _workRepo.GetLineageByAssetAsync(job.EntityId, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Lineage lookup failed for asset {EntityId} — work-level external_identifiers writes will be skipped",
                    job.EntityId);
            }
        }

        // Load pipeline configuration for this media type
        var pipelineConfig = _configLoader.LoadPipelines();
        var pipeline = pipelineConfig.GetPipelineForMediaType(job.MediaType);
        var strategy = pipeline.Strategy;
        var hydrationConfig = _configLoader.LoadHydration();

        var retailAcceptThreshold = hydrationConfig.RetailAutoAcceptThreshold;
        var retailAmbiguousThreshold = hydrationConfig.RetailAmbiguousThreshold;

        // Get ranked providers for this media type
        var providerConfigs = _configLoader.LoadAllProviders();
        var rankedProviders = pipeline.Providers.Count > 0
            ? pipeline.Providers.OrderBy(p => p.Rank).Select(p => p.Name).ToList()
            : providerConfigs.Select(p => p.Name).ToList();

        // Build hints from existing claims/canonicals
        var canonicals = await _canonicalRepo.GetByEntityAsync(job.EntityId, ct);
        var hints = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in canonicals)
        {
            hints.TryAdd(c.Key, c.Value);
        }

        var allCandidates = new List<RetailMatchCandidate>();
        RetailMatchCandidate? bestCandidate = null;
        var bestScore = 0.0;
        var providerRank = 0;
        var sequentialBridgeIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Iterate providers per strategy
        foreach (var providerName in rankedProviders)
        {
            providerRank++;
            var provider = _providers.FirstOrDefault(p =>
                string.Equals(p.Name, providerName, StringComparison.OrdinalIgnoreCase));

            if (provider is null) continue;

            try
            {
                // Build lookup request
                var lookupRequest = new ProviderLookupRequest
                {
                    EntityId = job.EntityId,
                    EntityType = EntityType.MediaAsset,
                    MediaType = mediaType,
                    Title = hints.GetValueOrDefault(MetadataFieldConstants.Title),
                    Author = hints.GetValueOrDefault(MetadataFieldConstants.Author),
                    Year = hints.GetValueOrDefault(MetadataFieldConstants.Year),
                    Narrator = hints.GetValueOrDefault(MetadataFieldConstants.Narrator),
                    ShowName = hints.GetValueOrDefault(MetadataFieldConstants.ShowName)
                        ?? hints.GetValueOrDefault(MetadataFieldConstants.Series),
                    Album = hints.GetValueOrDefault(MetadataFieldConstants.Album),
                    Artist = hints.GetValueOrDefault(MetadataFieldConstants.Artist),
                    Composer = hints.GetValueOrDefault(MetadataFieldConstants.Composer),
                    Director = hints.GetValueOrDefault(MetadataFieldConstants.Director),
                    SeasonNumber = hints.GetValueOrDefault(MetadataFieldConstants.SeasonNumber)
                        ?? hints.GetValueOrDefault("season"),
                    EpisodeNumber = hints.GetValueOrDefault(MetadataFieldConstants.EpisodeNumber)
                        ?? hints.GetValueOrDefault("episode"),
                    TrackNumber = hints.GetValueOrDefault(MetadataFieldConstants.TrackNumber),
                    Series = hints.GetValueOrDefault(MetadataFieldConstants.Series),
                    Genre = hints.GetValueOrDefault(MetadataFieldConstants.Genre),
                    Isbn = hints.GetValueOrDefault(BridgeIdKeys.Isbn),
                    Asin = hints.GetValueOrDefault(BridgeIdKeys.Asin),
                    Hints = hints,
                    PriorProviderBridgeIds = strategy == ProviderStrategy.Sequential
                        ? sequentialBridgeIds : null,
                };

                var claims = await provider.FetchAsync(lookupRequest, ct);
                if (claims.Count == 0) continue;

                // Extract candidate metadata from claims
                var candidateTitle = claims
                    .FirstOrDefault(c => string.Equals(c.Key, MetadataFieldConstants.Title,
                        StringComparison.OrdinalIgnoreCase))?.Value;
                var candidateAuthor = claims
                    .FirstOrDefault(c => string.Equals(c.Key, MetadataFieldConstants.Author,
                        StringComparison.OrdinalIgnoreCase))?.Value;
                var candidateYear = claims
                    .FirstOrDefault(c => string.Equals(c.Key, MetadataFieldConstants.Year,
                        StringComparison.OrdinalIgnoreCase))?.Value;

                var (structuralBonus, structuralEvidence) = ComputeSingleItemStructuralSignal(
                    mediaType, hints, claims);

                // Score candidate
                var retailScore = _retailScoring.ScoreCandidate(
                    hints, candidateTitle, candidateAuthor, candidateYear, mediaType,
                    structuralBonus: structuralBonus);

                var decision = EvaluateRetailDecision(
                    hints,
                    candidateTitle,
                    candidateAuthor,
                    candidateYear,
                    retailScore,
                    retailScore.CompositeScore,
                    retailAcceptThreshold,
                    retailAmbiguousThreshold,
                    "single_item");

                // Extract bridge IDs from claims
                var bridgeIdsJson = BuildBridgeIdsJson(claims);

                // Build candidate record
                var candidate = new RetailMatchCandidate
                {
                    JobId = job.Id,
                    ProviderId = provider.ProviderId,
                    ProviderName = provider.Name,
                    ProviderItemId = claims
                        .FirstOrDefault(c => string.Equals(c.Key, "provider_item_id",
                            StringComparison.OrdinalIgnoreCase))?.Value,
                    Rank = providerRank,
                    Title = candidateTitle ?? "(unknown)",
                    Creator = candidateAuthor,
                    Year = candidateYear,
                    ScoreTotal = decision.FinalScore,
                    ScoreBreakdownJson = BuildScoreBreakdownJson(
                        retailScore,
                        decision,
                        "single_item",
                        structuralEvidence,
                        structuralBonus),
                    BridgeIdsJson = bridgeIdsJson,
                    Description = claims
                        .FirstOrDefault(c => string.Equals(c.Key, MetadataFieldConstants.Description,
                            StringComparison.OrdinalIgnoreCase))?.Value,
                    ImageUrl = claims
                        .FirstOrDefault(c => string.Equals(c.Key, MetadataFieldConstants.CoverUrl,
                            StringComparison.OrdinalIgnoreCase))?.Value,
                    Outcome = decision.Outcome,
                };

                allCandidates.Add(candidate);

                // Track best candidate
                if (IsBetterCandidate(candidate, bestCandidate))
                {
                    bestScore = candidate.ScoreTotal;
                    bestCandidate = candidate;
                }

                // Persist claims if candidate is accepted or ambiguous
                if (decision.Outcome != "Rejected")
                {
                    // Phase 3c: pass lineage so parent-scope claims mirror
                    // onto the parent Work (book series → series Work,
                    // audiobook series → series Work, etc.).
                    await ScoringHelper.PersistAndScoreWithLineageAsync(
                        job.EntityId, claims, provider.ProviderId, lineage,
                        _claimRepo, _canonicalRepo, _scoringEngine, _configLoader, _providers, ct,
                        logger: _logger);

                    // Extract bridge IDs for Stage 2
                    var bridgeEntries = claims
                        .Where(c => BridgeIdHelper.IsBridgeId(c.Key) && !string.IsNullOrWhiteSpace(c.Value))
                        .Select(c => new BridgeIdEntry
                        {
                            EntityId = ResolveBridgeIdEntityId(lineage, job.EntityId, c.Key),
                            IdType = c.Key,
                            IdValue = c.Value,
                            ProviderId = provider.ProviderId.ToString(),
                        }).ToList();

                    if (bridgeEntries.Count > 0)
                        await _bridgeIdRepo.UpsertBatchAsync(bridgeEntries, ct);

                    // Phase 3b: also write provider bridge IDs to the appropriate
                    // Work's external_identifiers JSON. Track-level IDs land on
                    // the asset's own Work; album/show/series-level IDs land on
                    // the parent. WriteExternalIdentifiersAsync is no-overwrite,
                    // so re-running this for sibling tracks of the same album
                    // is harmless.
                    if (lineage is not null && bridgeEntries.Count > 0)
                    {
                        var bridgeDict = bridgeEntries
                            .GroupBy(b => b.IdType, StringComparer.OrdinalIgnoreCase)
                            .ToDictionary(g => g.Key, g => g.First().IdValue,
                                StringComparer.OrdinalIgnoreCase);

                        var (forParent, forSelf) = _claimRouter.SplitBridgeIds(lineage, bridgeDict);

                        if (forParent.Count > 0)
                        {
                            await _workRepo.WriteExternalIdentifiersAsync(
                                lineage.TargetForParentScope, forParent, ct);
                        }

                        if (forSelf.Count > 0)
                        {
                            await _workRepo.WriteExternalIdentifiersAsync(
                                lineage.TargetForSelfScope, forSelf, ct);
                        }
                    }

                    // Sequential: accumulate bridge IDs for next provider
                    if (strategy == ProviderStrategy.Sequential)
                    {
                        foreach (var c in claims.Where(c => BridgeIdHelper.IsBridgeId(c.Key)))
                            sequentialBridgeIds.TryAdd(c.Key, c.Value);
                    }
                }

                // Waterfall: stop after first accepted candidate
                if (strategy == ProviderStrategy.Waterfall && decision.Outcome == "AutoAccepted")
                    break;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex,
                    "Provider {Provider} failed for entity {EntityId}",
                    providerName, job.EntityId);
            }
        }

        // Persist ALL candidates (winners and losers)
        if (allCandidates.Count > 0)
            await _candidateRepo.InsertBatchAsync(allCandidates, ct);

        bestScore = bestCandidate?.ScoreTotal ?? 0.0;

        // Determine final job state based on best candidate
        if (bestCandidate is not null && bestCandidate.Outcome == "AutoAccepted")
        {
            await _jobRepo.SetSelectedCandidateAsync(job.Id, bestCandidate.Id, ct);
            await _jobRepo.UpdateStateAsync(job.Id, IdentityJobState.RetailMatched, ct: ct);
            await _timeline.RecordRetailMatchedAsync(
                job.EntityId, bestCandidate.ProviderName,
                allCandidates.Count, job.IngestionRunId, ct);

            _logger.LogInformation(
                "Retail match found for entity {EntityId}: '{Title}' from {Provider} (score: {Score:F2})",
                job.EntityId, bestCandidate.Title, bestCandidate.ProviderName, bestScore);

            try
            {
                await _postPipeline.EvaluateAndOrganizeAsync(
                    job.EntityId, job.Id, wikidataQid: null, job.IngestionRunId, ct)
                    .ConfigureAwait(false);
            }
            catch (Exception orgEx) when (orgEx is not OperationCanceledException)
            {
                _logger.LogWarning(orgEx,
                    "Post-retail organization failed for entity {EntityId} — pipeline continues",
                    job.EntityId);
            }
        }
        else if (bestCandidate is not null && bestCandidate.Outcome == "Ambiguous")
        {
            await _jobRepo.SetSelectedCandidateAsync(job.Id, bestCandidate.Id, ct);
            await _jobRepo.UpdateStateAsync(job.Id, IdentityJobState.RetailMatchedNeedsReview, ct: ct);
            await _outcomeFactory.CreateRetailAmbiguousAsync(
                job.EntityId, job.MediaType, bestScore, job.IngestionRunId, null, ct);
            await _timeline.RecordRetailMatchedAsync(
                job.EntityId, bestCandidate.ProviderName,
                allCandidates.Count, job.IngestionRunId, ct);

            _logger.LogInformation(
                "Retail match ambiguous for entity {EntityId}: '{Title}' (score: {Score:F2})",
                job.EntityId, bestCandidate.Title, bestScore);
        }
        else
        {
            await _jobRepo.UpdateStateAsync(job.Id, IdentityJobState.RetailNoMatch, ct: ct);

            var titleHint = hints.GetValueOrDefault(MetadataFieldConstants.Title);

            // If the file has a placeholder title with no bridge IDs, route to a
            // dedicated PlaceholderTitle review trigger instead of the generic
            // RetailMatchFailed bucket — these items will never match retail.
            if (PlaceholderTitleDetector.IsPlaceholder(titleHint)
                && !PlaceholderTitleDetector.HasBridgeId(hints))
            {
                await _outcomeFactory.CreatePlaceholderTitleAsync(
                    job.EntityId, titleHint, job.IngestionRunId, null, ct);
            }
            else
            {
                await _outcomeFactory.CreateRetailFailedAsync(
                    job.EntityId, job.MediaType, job.IngestionRunId, null, ct);
            }

            await _timeline.RecordRetailNoMatchAsync(
                job.EntityId, titleHint ?? "(unknown)", job.IngestionRunId, ct);

            _logger.LogInformation(
                "No retail match for entity {EntityId} — {CandidateCount} candidates evaluated, best score: {Score:F2}",
                job.EntityId, allCandidates.Count, bestScore);
        }
    }

    private sealed record RetailDecision(
        string Outcome,
        double FinalScore,
        string ThresholdPath,
        IReadOnlyList<string> RejectionReasons,
        double TextEvidence,
        bool CreatorPresentOnBothSides,
        bool CreatorContradiction,
        bool AutoAcceptBlocked,
        string MatchContext);

    private static Guid ResolveBridgeIdEntityId(WorkLineage? lineage, Guid assetId, string key)
    {
        if (lineage is null)
            return assetId;

        return ClaimScopeCatalog.IsParentScoped(key, lineage.MediaType)
            ? lineage.TargetForParentScope
            : assetId;
    }
}
