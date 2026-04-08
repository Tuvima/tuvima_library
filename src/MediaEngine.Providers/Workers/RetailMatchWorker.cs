using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using MediaEngine.Domain;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;
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
        ILogger<RetailMatchWorker> logger)
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
            ct);

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
                await ProcessJobAsync(job, ct);
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
            await ProcessMusicBatchAsync(musicJobs, ct);

        // Process TV jobs grouped by show+season (show_name+season_number key).
        if (tvJobs.Count > 0)
            await ProcessTvBatchAsync(tvJobs, ct);

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
    /// Processes a group of Music jobs (all from the same album) with a single
    /// Apple API album search + lookup call. Each job receives its per-track claims.
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

        // Use the first job's hints for the album-level search.
        var representativeHints = jobHints[groupJobs[0].EntityId];
        var artist = representativeHints.GetValueOrDefault(MetadataFieldConstants.Artist)
            ?? representativeHints.GetValueOrDefault(MetadataFieldConstants.Author);
        var album  = representativeHints.GetValueOrDefault(MetadataFieldConstants.Album);
        var country = "us";
        var lang    = "en";

        // Step 1: Search Apple for the album entity to get collectionId.
        _logger.LogInformation(
            "Music: searching Apple iTunes for album '{Album}' by '{Artist}' ({TrackCount} queued track(s))",
            album ?? "(unknown album)", artist ?? "(unknown artist)", groupJobs.Count);
        var collectionId = await SearchAppleAlbumAsync(artist, album, country, lang, ct);

        if (collectionId is null)
        {
            _logger.LogInformation(
                "Music: no album match for '{Album}' by '{Artist}' on Apple iTunes — falling back to per-track search for {TrackCount} job(s)",
                album ?? "(no album)", artist ?? "(no artist)", groupJobs.Count);

            // Fall back: process each job individually via the standard per-track path.
            foreach (var job in groupJobs)
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

        // Step 2: Fetch all tracks for the album via lookup?id={collectionId}&entity=song.
        var allTracks = await FetchAppleAlbumTracksAsync(collectionId, country, lang, ct);

        if (allTracks.Count == 0)
        {
            _logger.LogInformation(
                "RetailMatchWorker: Apple album lookup returned no tracks for collectionId={CollectionId}",
                collectionId);

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

        _logger.LogInformation(
            "Music: matched album '{Album}' on Apple iTunes (collectionId={CollectionId}, {TrackCount} tracks from API) — distributing to {JobCount} queued track(s)",
            album ?? "(unknown)", collectionId, allTracks.Count, groupJobs.Count);

        var appleProvider = _providers.FirstOrDefault(p =>
            string.Equals(p.Name, "apple_api", StringComparison.OrdinalIgnoreCase));

        // Step 3: For each job, find the best-matching track and apply its claims.
        foreach (var job in groupJobs)
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

        // Find best-matching track: prefer exact track number, fall back to title similarity.
        JsonNode? bestTrack = null;
        double bestMatchScore = -1.0;

        foreach (var track in allTracks)
        {
            var trackNumNode = track["trackNumber"]?.GetValue<long?>() is { } tn ? tn.ToString() : null;
            var trackName    = track["trackName"]?.GetValue<string>();

            double matchScore = 0.0;

            // Track number exact match is the strongest signal.
            if (!string.IsNullOrWhiteSpace(fileTrackNumber) && !string.IsNullOrWhiteSpace(trackNumNode)
                && string.Equals(fileTrackNumber.Trim(), trackNumNode.Trim(), StringComparison.Ordinal))
            {
                matchScore = 1.0;
            }
            else if (!string.IsNullOrWhiteSpace(fileTitle) && !string.IsNullOrWhiteSpace(trackName))
            {
                matchScore = ComputeWordOverlap(fileTitle, trackName);
            }

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

        var retailScore = _retailScoring.ScoreCandidate(
            fileHints, candidateTitle, candidateAuthor, candidateYear, MediaType.Music);

        string outcome;
        if (retailScore.CompositeScore >= retailAcceptThreshold)
            outcome = "AutoAccepted";
        else if (retailScore.CompositeScore >= retailAmbiguousThreshold)
            outcome = "Ambiguous";
        else
            outcome = "Rejected";

        var providerId = appleProvider?.ProviderId ?? Guid.Empty;

        var bridgeIdsJson = JsonSerializer.Serialize(
            claims.Where(c => BridgeIdHelper.IsBridgeId(c.Key))
                  .ToDictionary(c => c.Key, c => c.Value));

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
            ScoreTotal       = retailScore.CompositeScore,
            ScoreBreakdownJson = JsonSerializer.Serialize(new
            {
                title  = retailScore.TitleScore,
                author = retailScore.AuthorScore,
                year   = retailScore.YearScore,
                format = retailScore.FormatScore,
            }),
            BridgeIdsJson    = bridgeIdsJson,
            ImageUrl         = BuildAppleCoverUrl(bestTrack["artworkUrl100"]?.GetValue<string>()),
            Outcome          = outcome,
        };

        await _candidateRepo.InsertBatchAsync([candidate], ct);

        if (outcome != "Rejected")
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
                    EntityId   = job.EntityId,
                    IdType     = c.Key,
                    IdValue    = c.Value,
                    ProviderId = providerId.ToString(),
                }).ToList();

            if (bridgeEntries.Count > 0)
                await _bridgeIdRepo.UpsertBatchAsync(bridgeEntries, ct);
        }

        if (outcome == "AutoAccepted")
        {
            await _jobRepo.SetSelectedCandidateAsync(job.Id, candidate.Id, ct);
            await _jobRepo.UpdateStateAsync(job.Id, IdentityJobState.RetailMatched, ct: ct);
            await _timeline.RecordRetailMatchedAsync(
                job.EntityId, "apple_api", 1, job.IngestionRunId, ct);

            _logger.LogInformation(
                "Music: track '{FileTitle}' → '{MatchedTitle}' from Apple iTunes album lookup (score {Score:F2}) [entity {EntityId}]",
                fileTitle ?? "(unknown)", candidateTitle, retailScore.CompositeScore, job.EntityId);
        }
        else if (outcome == "Ambiguous")
        {
            await _jobRepo.SetSelectedCandidateAsync(job.Id, candidate.Id, ct);
            await _jobRepo.UpdateStateAsync(job.Id, IdentityJobState.RetailMatchedNeedsReview, ct: ct);
            await _outcomeFactory.CreateRetailAmbiguousAsync(
                job.EntityId, job.MediaType, retailScore.CompositeScore, job.IngestionRunId, null, ct);
            await _timeline.RecordRetailMatchedAsync(
                job.EntityId, "apple_api", 1, job.IngestionRunId, ct);

            _logger.LogInformation(
                "Music: track '{FileTitle}' → '{MatchedTitle}' ambiguous on Apple iTunes (score {Score:F2}, needs review) [entity {EntityId}]",
                fileTitle ?? "(unknown)", candidateTitle, retailScore.CompositeScore, job.EntityId);
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
                fileTitle ?? "(unknown)", retailScore.CompositeScore, job.EntityId);
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
        var lang        = "en";
        var country     = "US";

        // Step 1: Search TMDB for the show to get tv_id.
        _logger.LogInformation(
            "TV: searching TMDB for show '{ShowName}' — {EpisodeCount} episode(s) queued",
            showName ?? "(unknown)", groupJobs.Count);
        var tvId = await SearchTmdbShowAsync(showName, tmdbApiKey, lang, country, ct);

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
                    job, hints, allEpisodes, tvId,
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
    /// Searches TMDB for a TV show by name. Returns the tv_id string, or null if not found.
    /// </summary>
    private async Task<string?> SearchTmdbShowAsync(
        string? showName, string apiKey, string lang, string country, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(showName))
            return null;

        var query = Uri.EscapeDataString(showName.Trim());
        var url = $"https://api.themoviedb.org/3/search/tv?query={query}&include_adult=false&language={lang}-{country}&page=1&api_key={apiKey}";

        await ThrottleTmdbAsync(ct);

        try
        {
            using var client = _httpFactory.CreateClient("tmdb");
            var json = await client.GetFromJsonAsync<JsonNode>(url, ct);

            var results = json?["results"]?.AsArray();
            if (results is null || results.Count == 0)
                return null;

            // Pick the best match by show name similarity.
            double bestScore = 0.0;
            string? bestId = null;

            foreach (var result in results)
            {
                if (result is null) continue;

                var resultName = result["name"]?.GetValue<string>()
                    ?? result["original_name"]?.GetValue<string>();
                var resultId = result["id"]?.GetValue<long?>()?.ToString();

                if (string.IsNullOrWhiteSpace(resultName) || resultId is null)
                    continue;

                var score = ComputeWordOverlap(showName, resultName);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestId = resultId;
                }
            }

            if (bestScore >= 0.40)
            {
                _logger.LogInformation(
                    "TV: TMDB show search matched tv_id={Id} (score={Score:F2}) for '{ShowName}'",
                    bestId, bestScore, showName);
                return bestId;
            }

            _logger.LogInformation(
                "TV: TMDB show search — best score {Score:F2} below threshold for '{ShowName}'",
                bestScore, showName);
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "RetailMatchWorker: TMDB show search failed for '{ShowName}'", showName);
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
        var claims = BuildTvEpisodeClaims(bestEpisode, tvId, showName, fileSeason);

        // For retail scoring, the candidate title is the episode title and author/creator
        // is the show name (best available approximation for TV scoring).
        var candidateTitle  = bestEpisode["name"]?.GetValue<string>();
        var candidateAuthor = showName;
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
        if (seasonMatches && episodeMatches)
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

        string outcome;
        if (adjustedComposite >= retailAcceptThreshold)
            outcome = "AutoAccepted";
        else if (adjustedComposite >= retailAmbiguousThreshold)
            outcome = "Ambiguous";
        else
            outcome = "Rejected";

        var providerId = tmdbProvider?.ProviderId ?? Guid.Empty;

        var bridgeIdsJson = JsonSerializer.Serialize(
            claims.Where(c => BridgeIdHelper.IsBridgeId(c.Key))
                  .ToDictionary(c => c.Key, c => c.Value));

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
            ScoreTotal         = adjustedComposite,
            ScoreBreakdownJson = JsonSerializer.Serialize(new
            {
                title              = retailScore.TitleScore,
                author             = retailScore.AuthorScore,
                year               = retailScore.YearScore,
                format             = retailScore.FormatScore,
                structural_boost   = structuralAdjustment,
            }),
            BridgeIdsJson      = bridgeIdsJson,
            ImageUrl           = BuildTmdbImageUrl(bestEpisode["still_path"]?.GetValue<string>()),
            Outcome            = outcome,
        };

        await _candidateRepo.InsertBatchAsync([candidate], ct);

        if (outcome != "Rejected")
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
                    EntityId   = job.EntityId,
                    IdType     = c.Key,
                    IdValue    = c.Value,
                    ProviderId = providerId.ToString(),
                }).ToList();

            if (bridgeEntries.Count > 0)
                await _bridgeIdRepo.UpsertBatchAsync(bridgeEntries, ct);
        }

        if (outcome == "AutoAccepted")
        {
            await _jobRepo.SetSelectedCandidateAsync(job.Id, candidate.Id, ct);
            await _jobRepo.UpdateStateAsync(job.Id, IdentityJobState.RetailMatched, ct: ct);
            await _timeline.RecordRetailMatchedAsync(
                job.EntityId, "tmdb", 1, job.IngestionRunId, ct);

            _logger.LogInformation(
                "TV: '{ShowName}' S{Season}E{Episode} — '{EpisodeTitle}' matched on TMDB (score {Score:F2}) [entity {EntityId}]",
                showName ?? "(unknown)", fileSeason, fileEpisodeNumber ?? "?",
                candidateTitle, adjustedComposite, job.EntityId);
        }
        else if (outcome == "Ambiguous")
        {
            await _jobRepo.SetSelectedCandidateAsync(job.Id, candidate.Id, ct);
            await _jobRepo.UpdateStateAsync(job.Id, IdentityJobState.RetailMatchedNeedsReview, ct: ct);
            await _outcomeFactory.CreateRetailAmbiguousAsync(
                job.EntityId, job.MediaType, adjustedComposite, job.IngestionRunId, null, ct);
            await _timeline.RecordRetailMatchedAsync(
                job.EntityId, "tmdb", 1, job.IngestionRunId, ct);

            _logger.LogInformation(
                "TV: '{ShowName}' S{Season}E{Episode} — '{EpisodeTitle}' ambiguous on TMDB (score {Score:F2}, needs review) [entity {EntityId}]",
                showName ?? "(unknown)", fileSeason, fileEpisodeNumber ?? "?",
                candidateTitle, adjustedComposite, job.EntityId);
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
                adjustedComposite, job.EntityId);
        }
    }

    /// <summary>
    /// Builds <see cref="ProviderClaim"/> list from a raw TMDB episode JSON node.
    /// Includes show-level bridge ID (tmdb_id for the show) so Stage 2 can bridge to Wikidata.
    /// </summary>
    private static IReadOnlyList<ProviderClaim> BuildTvEpisodeClaims(
        JsonNode episode, string showTvId, string? showName, string season)
    {
        var claims = new List<ProviderClaim>();

        void Add(string key, string? value, double confidence)
        {
            if (!string.IsNullOrWhiteSpace(value))
                claims.Add(new ProviderClaim(key, value, confidence));
        }

        Add(MetadataFieldConstants.EpisodeTitle, episode["name"]?.GetValue<string>(), 0.85);

        // For TV, "title" in the system is typically the episode title.
        Add(MetadataFieldConstants.Title,         episode["name"]?.GetValue<string>(), 0.80);
        Add(MetadataFieldConstants.Description,   episode["overview"]?.GetValue<string>(), 0.85);
        Add(MetadataFieldConstants.ShowName,      showName, 0.85);

        var airDate = episode["air_date"]?.GetValue<string>();
        if (!string.IsNullOrWhiteSpace(airDate) && airDate.Length >= 4)
            Add(MetadataFieldConstants.Year, airDate[..4], 0.85);

        Add(MetadataFieldConstants.SeasonNumber,
            episode["season_number"]?.GetValue<long?>()?.ToString() ?? season, 0.90);
        Add(MetadataFieldConstants.EpisodeNumber,
            episode["episode_number"]?.GetValue<long?>()?.ToString(), 0.90);

        var stillPath = episode["still_path"]?.GetValue<string>();
        if (!string.IsNullOrWhiteSpace(stillPath))
            Add(MetadataFieldConstants.CoverUrl,
                $"https://image.tmdb.org/t/p/w500{stillPath}", 0.85);

        // The show-level TMDB ID is the critical bridge ID for Stage 2 Wikidata resolution.
        // Episode-level TMDB IDs are available but the show QID is what Wikidata resolves.
        Add(BridgeIdKeys.TmdbId, showTvId, 1.0);

        var rating = episode["vote_average"]?.GetValue<double?>()?.ToString("F1");
        if (!string.IsNullOrWhiteSpace(rating))
            Add(MetadataFieldConstants.Rating, rating, 0.80);

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

    // ── Grouping key helpers ─────────────────────────────────────────────────

    private static string BuildAlbumKey(Dictionary<string, string> hints)
    {
        hints.TryGetValue(MetadataFieldConstants.Artist, out var artist);
        if (string.IsNullOrWhiteSpace(artist))
            hints.TryGetValue(MetadataFieldConstants.Author, out artist);

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
        return [.. text.ToLowerInvariant()
            .Split([' ', ',', '.', '-', ':', ';', '\'', '"', '(', ')', '[', ']'],
                   StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length >= 2)];
    }

    // ── Per-item fallback (Books, Audiobooks, Movies, Comics, Podcasts) ──────

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
                    Narrator = hints.GetValueOrDefault(MetadataFieldConstants.Narrator),
                    ShowName = hints.GetValueOrDefault(MetadataFieldConstants.ShowName)
                        ?? hints.GetValueOrDefault(MetadataFieldConstants.Series),
                    Album = hints.GetValueOrDefault(MetadataFieldConstants.Album),
                    Artist = hints.GetValueOrDefault(MetadataFieldConstants.Artist),
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

                // Score candidate
                var retailScore = _retailScoring.ScoreCandidate(
                    hints, candidateTitle, candidateAuthor, candidateYear, mediaType);

                // Determine candidate outcome
                string outcome;
                if (retailScore.CompositeScore >= retailAcceptThreshold)
                    outcome = "AutoAccepted";
                else if (retailScore.CompositeScore >= retailAmbiguousThreshold)
                    outcome = "Ambiguous";
                else
                    outcome = "Rejected";

                // Extract bridge IDs from claims
                var bridgeIdsJson = System.Text.Json.JsonSerializer.Serialize(
                    claims.Where(c => BridgeIdHelper.IsBridgeId(c.Key))
                          .ToDictionary(c => c.Key, c => c.Value));

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
                    ScoreTotal = retailScore.CompositeScore,
                    ScoreBreakdownJson = System.Text.Json.JsonSerializer.Serialize(new
                    {
                        title = retailScore.TitleScore,
                        author = retailScore.AuthorScore,
                        year = retailScore.YearScore,
                        format = retailScore.FormatScore,
                    }),
                    BridgeIdsJson = bridgeIdsJson,
                    Description = claims
                        .FirstOrDefault(c => string.Equals(c.Key, MetadataFieldConstants.Description,
                            StringComparison.OrdinalIgnoreCase))?.Value,
                    ImageUrl = claims
                        .FirstOrDefault(c => string.Equals(c.Key, MetadataFieldConstants.CoverUrl,
                            StringComparison.OrdinalIgnoreCase))?.Value,
                    Outcome = outcome,
                };

                allCandidates.Add(candidate);

                // Track best candidate
                if (retailScore.CompositeScore > bestScore)
                {
                    bestScore = retailScore.CompositeScore;
                    bestCandidate = candidate;
                }

                // Persist claims if candidate is accepted or ambiguous
                if (outcome != "Rejected")
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
                            EntityId = job.EntityId,
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
                if (strategy == ProviderStrategy.Waterfall && outcome == "AutoAccepted")
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

        // Determine final job state based on best candidate
        if (bestCandidate is not null && bestScore >= retailAcceptThreshold)
        {
            await _jobRepo.SetSelectedCandidateAsync(job.Id, bestCandidate.Id, ct);
            await _jobRepo.UpdateStateAsync(job.Id, IdentityJobState.RetailMatched, ct: ct);
            await _timeline.RecordRetailMatchedAsync(
                job.EntityId, bestCandidate.ProviderName,
                allCandidates.Count, job.IngestionRunId, ct);

            _logger.LogInformation(
                "Retail match found for entity {EntityId}: '{Title}' from {Provider} (score: {Score:F2})",
                job.EntityId, bestCandidate.Title, bestCandidate.ProviderName, bestScore);
        }
        else if (bestCandidate is not null && bestScore >= retailAmbiguousThreshold)
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
}
