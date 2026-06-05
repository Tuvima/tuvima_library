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
using Microsoft.Extensions.Logging.Abstractions;

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
    private readonly ICanonicalValueArrayRepository? _arrayRepo;
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
    private readonly AppleRetailClient _appleClient;
    private readonly TmdbRetailClient _tmdbClient;
    private readonly RetailCandidateScorer _candidateScorer;
    private readonly ILogger<RetailMatchWorker> _logger;

    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Cross-file batching window. Sourced from
    /// <c>config/core.json → pipeline.lease_sizes.retail</c> at construction time.
    /// Larger values mean a single drop of N files (e.g. a TV season, an album)
    /// processes in one lease cycle instead of being chopped into multiple leases
    /// — which is what enables one Apple album call to cover all its tracks.
    /// </summary>

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
        IAssetExportService? assetExportService = null,
        ICanonicalValueArrayRepository? arrayRepo = null,
        AppleRetailClient? appleClient = null,
        TmdbRetailClient? tmdbClient = null,
        RetailCandidateScorer? candidateScorer = null)
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
        _arrayRepo = arrayRepo;
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
        _appleClient = appleClient ?? new AppleRetailClient(
            _httpFactory,
            new RetailRequestBuilder(),
            new RetailHttpThrottle(),
            NullLogger<AppleRetailClient>.Instance);
        _tmdbClient = tmdbClient ?? new TmdbRetailClient(
            _httpFactory,
            new RetailRequestBuilder(),
            new RetailHttpThrottle(),
            NullLogger<TmdbRetailClient>.Instance);
        _candidateScorer = candidateScorer ?? new RetailCandidateScorer();
        _logger = logger;

        // Lease size is read once at construction. A restart applies any
        // config change — same lifetime as every other CoreConfiguration value.
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
            GetBatchSize(),
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
                await IdentityJobRetryPolicy.ScheduleRetryOrDeadLetterAsync(
                    _jobRepo,
                    job,
                    IdentityJobState.Queued,
                    ex,
                    _configLoader.LoadHydration(),
                    ct);
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

    private int GetBatchSize() =>
        Math.Max(1, _configLoader.LoadCore().Pipeline.LeaseSizes.Retail);

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
            jobHints[job.EntityId] = await BuildFileHintsAsync(job.EntityId, ct);
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
                        await IdentityJobRetryPolicy.ScheduleRetryOrDeadLetterAsync(
                            _jobRepo,
                            job,
                            IdentityJobState.Queued,
                            innerEx,
                            _configLoader.LoadHydration(),
                            ct);
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
        MediaEngine.Providers.Services.AppleTrackSearchMatch? trackSearchMatch = null;
        string? collectionId = null;
        var resolvedVia = "track search";

        var providerConfigs = _configLoader.LoadAllProviders();
        var appleProvider = ProviderExecutionFilter.FindEnabledProvider(
            _providers,
            providerConfigs,
            "apple_api");

        if (appleProvider is null)
        {
            _logger.LogInformation(
                "Music: Apple provider is disabled or unavailable; falling back to generic retail matching for {TrackCount} queued track(s)",
                orderedGroupJobs.Count);

            foreach (var job in orderedGroupJobs)
            {
                try { await ProcessJobAsync(job, ct); }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex,
                        "RetailMatchWorker generic music fallback failed for {EntityId}", job.EntityId);
                    await _jobRepo.UpdateStateAsync(job.Id, IdentityJobState.Failed, ex.Message, ct);
                }
            }

            return;
        }

        if (orderedGroupJobs.Count == 1)
        {
            _logger.LogInformation(
                "Music: searching Apple iTunes by track '{Title}' / '{Artist}' ({TrackCount} queued track(s))",
                title ?? "(unknown)", artist ?? "(unknown artist)", orderedGroupJobs.Count);

            trackSearchMatch = await _appleClient.SearchTrackAsync(artist, title, album, country, lang, ct);
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

                var match = await _appleClient.SearchTrackAsync(artist, currentTitle, album, country, lang, ct);
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
            collectionId = await _appleClient.SearchAlbumAsync(artist, album, country, lang, ct);
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
        var allTracks = await _appleClient.FetchAlbumTracksAsync(collectionId, country, lang, ct);

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
                ? RetailTextSimilarity.ComputeWordOverlap(fileTitle, trackName)
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
            if (await TryRouteMusicLocalIdentityFallbackAsync(job, fileHints, bestMatchScore, ct)
                    .ConfigureAwait(false))
            {
                return;
            }

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
        var fileAlbum = fileHints.GetValueOrDefault(MetadataFieldConstants.Album);
        var candidateAlbum = bestTrack["collectionName"]?.GetValue<string>();
        var albumCorroborates = !string.IsNullOrWhiteSpace(fileAlbum)
            && RetailTextSimilarity.AreEquivalentNames(fileAlbum, candidateAlbum);
        var yearCorroborates = retailScore.YearScore >= 0.80;
        var singleTrackRelease = candidateTrackCount == 1;
        var strongSingleTrackIdentity = singleTrackRelease
            && retailScore.TitleScore >= 0.95
            && retailScore.AuthorScore >= 0.85;
        var strongCanonicalTrackIdentity = retailScore.TitleScore >= 0.95
            && retailScore.AuthorScore >= 0.85
            && (albumCorroborates || yearCorroborates);
        var decision = _candidateScorer.EvaluateDecision(
            fileHints,
            candidateTitle,
            candidateAuthor,
            candidateYear,
            retailScore,
            retailScore.CompositeScore,
            retailAcceptThreshold,
            retailAmbiguousThreshold,
            "grouped_music",
            autoAcceptCapReasons: trackNumberMatches
                || durationCorroborates
                || strongSingleTrackIdentity
                || strongCanonicalTrackIdentity
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
            ScoreBreakdownJson = _candidateScorer.BuildScoreBreakdownJson(
                retailScore,
                decision,
                "grouped_music",
                new Dictionary<string, object?>
                {
                    ["track_match_score"] = Math.Round(bestMatchScore, 4),
                    ["track_number_matches"] = trackNumberMatches,
                    ["duration_corroborates"] = durationCorroborates,
                    ["album_corroborates"] = albumCorroborates,
                    ["year_corroborates"] = yearCorroborates,
                    ["single_track_release"] = singleTrackRelease,
                    ["strong_single_track_identity"] = strongSingleTrackIdentity,
                    ["strong_canonical_track_identity"] = strongCanonicalTrackIdentity,
                    ["file_duration_seconds"] = hasFileDuration ? fileDurationSeconds : null,
                    ["candidate_duration_seconds"] = hasCandidateDuration ? candidateDurationSeconds : null,
                }),
            BridgeIdsJson    = bridgeIdsJson,
            ImageUrl         = RetailRequestBuilder.BuildAppleCoverUrl(bestTrack["artworkUrl100"]?.GetValue<string>()),
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
                arrayRepo: _arrayRepo, logger: _logger);

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
            if (await TryRouteMusicLocalIdentityFallbackAsync(job, fileHints, decision.FinalScore, ct)
                    .ConfigureAwait(false))
            {
                return;
            }

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
        var artworkUrl = RetailRequestBuilder.BuildAppleCoverUrl(track["artworkUrl100"]?.GetValue<string>());
        if (!string.IsNullOrWhiteSpace(artworkUrl))
            claims.Add(new ProviderClaim(MetadataFieldConstants.CoverUrl, artworkUrl, 0.90));

        return claims;
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
            jobHints[job.EntityId] = await BuildFileHintsAsync(job.EntityId, ct);
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
        if (!ProviderExecutionFilter.IsEnabled("tmdb", providerConfigs))
        {
            _logger.LogInformation(
                "RetailMatchWorker: TMDB provider disabled; falling back to generic retail matching for {Count} TV job(s)",
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
        var showSearch = await _tmdbClient.SearchShowAsync(showName, yearHint, tmdbApiKey, lang, country, ct);
        var tvId = showSearch.TvId;
        var showPosterPath = showSearch.PosterPath;
        var matchedShowName = showSearch.MatchedShowName;

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

        var showDetails = await _tmdbClient.FetchShowDetailsAsync(tvId, tmdbApiKey, lang, country, ct);

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

            var episodes = await _tmdbClient.FetchSeasonEpisodesAsync(tvId, seasonNumber, tmdbApiKey, lang, country, ct);
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
                matchScore = RetailTextSimilarity.ComputeWordOverlap(fileTitle, epTitle);
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
        var showPosterUrl = RetailRequestBuilder.BuildTmdbImageUrl(showPosterPath);
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
        bool showMatches = RetailTextSimilarity.AreEquivalentNames(showName, providerShowName);

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

        var decision = _candidateScorer.EvaluateDecision(
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
            ScoreBreakdownJson = _candidateScorer.BuildScoreBreakdownJson(
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
                arrayRepo: _arrayRepo, logger: _logger);

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
        Add(MetadataFieldConstants.ShortDescription, showDetails?["overview"]?.GetValue<string>(), 0.84);
        Add(MetadataFieldConstants.Tagline, showDetails?["tagline"]?.GetValue<string>(), 0.78);
        Add(MetadataFieldConstants.Network, showDetails?["networks"]?[0]?["name"]?.GetValue<string>(), 0.85);
        Add(MetadataFieldConstants.Cover, RetailRequestBuilder.BuildTmdbImageUrl(showDetails?["poster_path"]?.GetValue<string>()) ?? fallbackPosterUrl, 0.90);
        Add(BridgeIdKeys.TmdbId, showTvId, 1.0);
        Add(MetadataFieldConstants.Rating, showDetails?["vote_average"]?.GetValue<double?>()?.ToString("F1"), 0.80);
        Add("content_rating", ExtractTmdbTvContentRating(showDetails), 0.88);
        Add(MetadataFieldConstants.OriginalLanguage, showDetails?["original_language"]?.GetValue<string>(), 0.85);

        var firstAirDate = showDetails?["first_air_date"]?.GetValue<string>();
        if (!string.IsNullOrWhiteSpace(firstAirDate) && firstAirDate.Length >= 4)
            Add(MetadataFieldConstants.Year, firstAirDate[..4], 0.85);

        return claims;
    }

    private static string? ExtractTmdbTvContentRating(JsonNode? showDetails)
    {
        var results = showDetails?["content_ratings"]?["results"]?.AsArray();
        if (results is null)
            return null;

        foreach (var country in new[] { "US", "GB", "CA", "AU" })
        {
            var rating = results.FirstOrDefault(node =>
                string.Equals(node?["iso_3166_1"]?.GetValue<string>(), country, StringComparison.OrdinalIgnoreCase))
                ?["rating"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(rating))
                return rating;
        }

        return null;
    }

    private async Task DownloadAndPersistTmdbEpisodeStillAsync(
        JsonNode episode,
        Guid episodeWorkId,
        CancellationToken ct)
    {
        if (_entityAssetRepo is null || _assetPaths is null)
            return;

        var stillUrl = RetailRequestBuilder.BuildTmdbImageUrl(episode["still_path"]?.GetValue<string>());
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

    private static CandidateExtendedMetadata BuildCandidateExtendedMetadata(
        IReadOnlyList<ProviderClaim> claims)
    {
        static string? First(IReadOnlyList<ProviderClaim> claims, params string[] keys)
        {
            foreach (var key in keys)
            {
                var value = claims.FirstOrDefault(c =>
                    string.Equals(c.Key, key, StringComparison.OrdinalIgnoreCase))?.Value;
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }

            return null;
        }

        var genre = First(claims, MetadataFieldConstants.Genre);

        return new CandidateExtendedMetadata
        {
            Description = First(claims, MetadataFieldConstants.Description),
            Publisher = First(claims, MetadataFieldConstants.PublisherField, "publisher"),
            Genres = string.IsNullOrWhiteSpace(genre)
                ? null
                : genre.Split(',', ';', '|')
                    .Select(part => part.Trim())
                    .Where(part => part.Length > 0)
                    .ToArray(),
            Language = First(claims, "language"),
            Series = First(claims, MetadataFieldConstants.Series),
            IssueNumber = First(claims, "issue_number", MetadataFieldConstants.SeriesPosition, "issue"),
        };
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

            var seriesMatches = RetailTextSimilarity.AreEquivalentNames(fileSeries, candidateSeries);
            var issueMatches = AreEquivalentOrdinals(fileIssue, candidateIssue);
            var titleMatches = RetailTextSimilarity.AreEquivalentNames(fileTitle, candidateTitle);
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
        var chars = RetailTextSimilarity.StripDiacritics(text)
            .Replace("&", " and ", StringComparison.Ordinal)
            .ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : ' ')
            .ToArray();

        return string.Join(' ', new string(chars)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));
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

    private sealed record MusicGroupTrackSearchEvidence(
        Guid EntityId,
        string Title,
        MediaEngine.Providers.Services.AppleTrackSearchMatch Match);

    private sealed record MusicGroupCollectionSelection(
        string CollectionId,
        int SupportCount,
        int AlbumExactCount,
        double TotalAlbumScore,
        double TotalScore);

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

    private async Task<Dictionary<string, string>> BuildFileHintsAsync(Guid entityId, CancellationToken ct)
    {
        var hints = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var canonicals = await _canonicalRepo.GetByEntityAsync(entityId, ct);
        foreach (var c in canonicals)
        {
            if (!string.IsNullOrWhiteSpace(c.Key) && !string.IsNullOrWhiteSpace(c.Value))
                hints.TryAdd(c.Key, TextEncodingRepair.RepairMojibake(c.Value));
        }

        if (_arrayRepo is not null)
        {
            var arrays = await _arrayRepo.GetAllByEntityAsync(entityId, ct);
            foreach (var (key, entries) in arrays)
            {
                if (hints.ContainsKey(key))
                    continue;

                var values = entries
                    .OrderBy(entry => entry.Ordinal)
                    .Select(entry => TextEncodingRepair.RepairMojibake(entry.Value))
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (values.Count > 0)
                    hints.TryAdd(key, JoinHintValues(key, values));
            }
        }

        var claims = await _claimRepo.GetByEntityAsync(entityId, ct);
        foreach (var group in claims
            .Where(claim => !string.IsNullOrWhiteSpace(claim.ClaimKey)
                && !string.IsNullOrWhiteSpace(claim.ClaimValue))
            .GroupBy(claim => claim.ClaimKey, StringComparer.OrdinalIgnoreCase))
        {
            if (hints.ContainsKey(group.Key))
                continue;

            var values = group
                .OrderByDescending(claim => claim.IsUserLocked)
                .ThenByDescending(claim => claim.Confidence)
                .ThenByDescending(claim => claim.ClaimedAt)
                .Select(claim => TextEncodingRepair.RepairMojibake(claim.ClaimValue.Trim()))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (values.Count > 0)
                hints.TryAdd(group.Key, JoinHintValues(group.Key, values));
        }

        return hints;
    }

    private static string JoinHintValues(string key, IReadOnlyList<string> values)
    {
        if (values.Count == 1 || !IsMultiValueCreatorHint(key))
            return values[0];

        return string.Join(" and ", values);
    }

    private static bool IsMultiValueCreatorHint(string key) =>
        key.Equals(MetadataFieldConstants.Author, StringComparison.OrdinalIgnoreCase)
        || key.Equals(MetadataFieldConstants.Artist, StringComparison.OrdinalIgnoreCase)
        || key.Equals(MetadataFieldConstants.Composer, StringComparison.OrdinalIgnoreCase)
        || key.Equals(MetadataFieldConstants.Director, StringComparison.OrdinalIgnoreCase)
        || key.Equals(MetadataFieldConstants.Narrator, StringComparison.OrdinalIgnoreCase)
        || key.Equals(MetadataFieldConstants.Illustrator, StringComparison.OrdinalIgnoreCase)
        || key.Equals("writer", StringComparison.OrdinalIgnoreCase);

    private async Task<bool> TryRouteMusicLocalIdentityFallbackAsync(
        IdentityJob job,
        IReadOnlyDictionary<string, string> hints,
        double bestRetailScore,
        CancellationToken ct)
    {
        if (!Enum.TryParse<MediaType>(job.MediaType, true, out var mediaType)
            || mediaType != MediaType.Music)
        {
            return false;
        }

        var title = hints.GetValueOrDefault(MetadataFieldConstants.Title);
        var artist = hints.GetValueOrDefault(MetadataFieldConstants.Artist)
            ?? hints.GetValueOrDefault(MetadataFieldConstants.Author)
            ?? hints.GetValueOrDefault(MetadataFieldConstants.Composer);

        if (PlaceholderTitleDetector.IsPlaceholder(title)
            || string.IsNullOrWhiteSpace(artist))
        {
            return false;
        }

        await _jobRepo.UpdateStateAsync(
            job.Id,
            IdentityJobState.RetailMatchedNeedsReview,
            "Retail did not accept a music match; attempting Wikidata from local title and artist.",
            ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Music identity fallback queued for entity {EntityId}: '{Title}' by '{Artist}' (best retail score {Score:F2})",
            job.EntityId,
            title,
            artist,
            bestRetailScore);

        return true;
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
        var enabledProviders = ProviderExecutionFilter.EnabledProviderNames(
            rankedProviders,
            _providers,
            providerConfigs);
        if (enabledProviders.Count == 0)
        {
            var message = $"No enabled retail provider is configured for media type '{job.MediaType}'.";
            await _jobRepo.ScheduleRetryAsync(
                job.Id,
                IdentityJobState.Queued,
                DateTimeOffset.UtcNow.AddMinutes(30),
                message,
                ct).ConfigureAwait(false);

            _logger.LogWarning(
                "RetailMatchWorker: {Message} Entity {EntityId} will stay queued instead of becoming no-match.",
                message,
                job.EntityId);
            return;
        }

        // Build hints from existing canonicals plus claim fallbacks. Some local
        // processor evidence, especially authors, can be multi-valued and may
        // not have a scalar canonical yet.
        var hints = await BuildFileHintsAsync(job.EntityId, ct);

        var allCandidates = new List<RetailMatchCandidate>();
        RetailMatchCandidate? bestCandidate = null;
        var bestScore = 0.0;
        var providerRank = 0;
        var providerFailures = 0;
        var sequentialBridgeIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Iterate providers per strategy
        foreach (var providerName in enabledProviders)
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
                var extendedMetadata = BuildCandidateExtendedMetadata(claims);

                // Score candidate
                var retailScore = _retailScoring.ScoreCandidate(
                    hints, candidateTitle, candidateAuthor, candidateYear, mediaType,
                    extendedMetadata: extendedMetadata,
                    structuralBonus: structuralBonus);

                var decision = _candidateScorer.EvaluateDecision(
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
                    ScoreBreakdownJson = _candidateScorer.BuildScoreBreakdownJson(
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
                        arrayRepo: _arrayRepo, logger: _logger);

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
                providerFailures++;
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
        else if (providerFailures > 0)
        {
            var message = $"Retail provider lookup failed for {providerFailures} provider(s); retrying before no-match classification.";
            await _jobRepo.ScheduleRetryAsync(
                job.Id,
                IdentityJobState.Queued,
                DateTimeOffset.UtcNow.AddMinutes(10),
                message,
                ct).ConfigureAwait(false);

            _logger.LogWarning(
                "RetailMatchWorker: {Message} Entity {EntityId}; candidates evaluated: {CandidateCount}, best score: {Score:F2}",
                message,
                job.EntityId,
                allCandidates.Count,
                bestScore);
        }
        else
        {
            if (await TryRouteMusicLocalIdentityFallbackAsync(job, hints, bestScore, ct)
                    .ConfigureAwait(false))
            {
                return;
            }

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

    private static Guid ResolveBridgeIdEntityId(WorkLineage? lineage, Guid assetId, string key)
    {
        if (lineage is null)
            return assetId;

        return ClaimScopeCatalog.IsParentScoped(key, lineage.MediaType)
            ? lineage.TargetForParentScope
            : lineage.TargetForSelfScope;
    }

}
