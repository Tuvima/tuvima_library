using System.Net;
using System.Text;
using Dapper;
using MediaEngine.Domain.Aggregates;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Models;
using MediaEngine.Domain.Services;
using MediaEngine.Providers.Services;
using MediaEngine.Storage;
using MediaEngine.Storage.Contracts;
using MediaEngine.Storage.Models;
using Microsoft.Extensions.Logging.Abstractions;
using StorageHttpClientConfig = MediaEngine.Storage.Models.HttpClientConfig;
using StorageProviderConfiguration = MediaEngine.Storage.Models.ProviderConfiguration;

namespace MediaEngine.Providers.Tests;

public sealed class ImageEnrichmentServiceTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _libraryRoot;
    private readonly string _configRoot;
    private readonly DatabaseConnection _db;
    private readonly EntityAssetRepository _entityAssets;
    private readonly MediaAssetRepository _mediaAssets;
    private readonly CanonicalValueRepository _canonicals;
    private readonly WorkRepository _works;
    private readonly ImageCacheRepository _imageCache;
    private readonly AssetPathService _assetPaths;
    private readonly ImagePathService _imagePaths;
    private readonly ConfigurationDirectoryLoader _configLoader;

    public ImageEnrichmentServiceTests()
    {
        DapperConfiguration.Configure();

        _tempRoot = Path.Combine(Path.GetTempPath(), $"tuvima_image_enrichment_{Guid.NewGuid():N}");
        _libraryRoot = Path.Combine(_tempRoot, "library");
        _configRoot = Path.Combine(_tempRoot, "config");
        Directory.CreateDirectory(_libraryRoot);

        _db = new DatabaseConnection(Path.Combine(_tempRoot, "library.db"));
        _db.InitializeSchema();
        _db.RunStartupChecks();

        _entityAssets = new EntityAssetRepository(_db);
        _mediaAssets = new MediaAssetRepository(_db);
        _canonicals = new CanonicalValueRepository(_db);
        _works = new WorkRepository(_db);
        _imageCache = new ImageCacheRepository(_db);
        _assetPaths = new AssetPathService(_libraryRoot);
        _imagePaths = new ImagePathService(_libraryRoot);
        _configLoader = new ConfigurationDirectoryLoader(_configRoot);
        _configLoader.SaveProvider(new StorageProviderConfiguration
        {
            Name = "fanart_tv",
            Enabled = true,
            HttpClient = new StorageHttpClientConfig
            {
                ApiKey = "test-key",
                TimeoutSeconds = 10,
            },
        });
    }

    public void Dispose()
    {
        try { _db.Dispose(); } catch { }
        try { Directory.Delete(_tempRoot, recursive: true); } catch { }
    }

    [Fact]
    public async Task EnrichWorkImagesAsync_MovieAlternatePosterAndClearArt_PreservesUserCoverOverride()
    {
        var movie = await SeedStandaloneAssetAsync(MediaType.Movies, "Movies", "Movies", "Arrival (2016).mkv");
        await SeedCanonicalsAsync(
            movie.WorkId,
            ("media_type", "Movies"),
            ("tmdb_movie_id", "12345"));

        var existingCoverId = Guid.NewGuid();
        var existingCoverPath = _assetPaths.GetCentralAssetPath("Work", movie.WorkId, "CoverArt", existingCoverId, ".jpg");
        AssetPathService.EnsureDirectory(existingCoverPath);
        await File.WriteAllBytesAsync(existingCoverPath, [9, 9, 9]);

        await _entityAssets.UpsertAsync(new EntityAsset
        {
            Id = existingCoverId,
            EntityId = movie.WorkId.ToString(),
            EntityType = "Work",
            AssetTypeValue = "CoverArt",
            LocalImagePath = existingCoverPath,
            SourceProvider = "user_upload",
            AssetClassValue = "Artwork",
            StorageLocationValue = "Central",
            OwnerScope = "Work",
            IsPreferred = true,
            IsUserOverride = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        var service = CreateService(request =>
        {
            var url = request.RequestUri?.ToString() ?? string.Empty;
            if (url.Contains("/movies/12345?", StringComparison.OrdinalIgnoreCase))
            {
                var payload = """
                    {
                      "movieposter": [
                        { "url": "https://images.test/movie-poster.jpg", "likes": "7", "lang": "en" }
                      ],
                      "hdmovieclearart": [
                        { "url": "https://images.test/movie-clear.png", "likes": "11", "lang": "en" }
                      ]
                    }
                    """;
                return JsonResponse(payload);
            }

            return ImageResponse([1, 2, 3, 4]);
        });

        await service.EnrichWorkImagesAsync(movie.AssetId, "Q12345");

        var coverAssets = await _entityAssets.GetByEntityAsync(movie.WorkId.ToString(), "CoverArt");
        var userCover = Assert.Single(coverAssets, asset => asset.IsUserOverride);
        Assert.True(userCover.IsPreferred);
        Assert.Contains(coverAssets, asset => !asset.IsUserOverride && !asset.IsPreferred && string.Equals(asset.SourceProvider, "fanart_tv", StringComparison.OrdinalIgnoreCase));

        var clearArt = Assert.Single(await _entityAssets.GetByEntityAsync(movie.WorkId.ToString(), "ClearArt"));
        Assert.True(clearArt.IsPreferred);
        Assert.EndsWith(".png", clearArt.LocalImagePath, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(clearArt.LocalImagePath));
    }

    [Fact]
    public async Task EnrichWorkImagesAsync_MusicCdart_StoresDiscArt()
    {
        var album = await SeedStandaloneAssetAsync(MediaType.Music, "Music", "Music", Path.Combine("Artist", "Album", "01 - Track.flac"));
        await SeedCanonicalsAsync(
            album.WorkId,
            ("media_type", "Music"),
            ("musicbrainz_release_group_id", "rg-123"));

        var service = CreateService(request =>
        {
            var url = request.RequestUri?.ToString() ?? string.Empty;
            if (url.Contains("/music/albums/rg-123?", StringComparison.OrdinalIgnoreCase))
            {
                var payload = """
                    {
                      "albums": {
                        "rg-123": {
                          "cdart": [
                            { "url": "https://images.test/album-disc.png", "likes": "4", "lang": "en" }
                          ]
                        }
                      }
                    }
                    """;
                return JsonResponse(payload);
            }

            return ImageResponse([5, 6, 7, 8]);
        });

        await service.EnrichWorkImagesAsync(album.AssetId, "QALBUM");

        var discArt = Assert.Single(await _entityAssets.GetByEntityAsync(album.WorkId.ToString(), "DiscArt"));
        Assert.True(discArt.IsPreferred);
        Assert.Equal("Work", discArt.OwnerScope);
        Assert.EndsWith(".png", discArt.LocalImagePath, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(discArt.LocalImagePath));
    }

    [Fact]
    public async Task EnrichWorkImagesAsync_TvSeasonAndEpisodeArt_AttachesToResolvedChildWorks()
    {
        var show = await _works.InsertParentAsync(MediaType.TV, "show:the-expanse", null, null);
        var season = await _works.InsertChildAsync(MediaType.TV, show, 1);
        var episode = await _works.InsertChildAsync(MediaType.TV, season, 2);
        var asset = await SeedAssetForExistingWorkAsync(episode, Path.Combine("TV", "The Expanse", "Season 01", "The Expanse - s01e02 - Episode.mkv"));

        await SeedCanonicalsAsync(
            show,
            ("media_type", "TV"),
            ("tvdb_id", "54321"));

        var service = CreateService(request =>
        {
            var url = request.RequestUri?.ToString() ?? string.Empty;
            if (url.Contains("/tv/54321?", StringComparison.OrdinalIgnoreCase))
            {
                var payload = """
                    {
                      "seasonposter": [
                        { "url": "https://images.test/season-poster.jpg", "likes": "8", "lang": "en", "season": "1" }
                      ],
                      "seasonthumb": [
                        { "url": "https://images.test/season-thumb.jpg", "likes": "5", "lang": "en", "season": "1" }
                      ],
                      "tvthumb": [
                        { "url": "https://images.test/episode-still.jpg", "likes": "6", "lang": "en", "season": "1", "episode": "2" }
                      ]
                    }
                    """;
                return JsonResponse(payload);
            }

            return ImageResponse([7, 7, 7, 7]);
        });

        await service.EnrichWorkImagesAsync(asset.AssetId, "QSHOW");

        var seasonPoster = Assert.Single(await _entityAssets.GetByEntityAsync(season.ToString(), "SeasonPoster"));
        var seasonThumb = Assert.Single(await _entityAssets.GetByEntityAsync(season.ToString(), "SeasonThumb"));
        var episodeStill = Assert.Single(await _entityAssets.GetByEntityAsync(episode.ToString(), "EpisodeStill"));

        Assert.Equal("Season", seasonPoster.OwnerScope);
        Assert.Equal("Season", seasonThumb.OwnerScope);
        Assert.Equal("Episode", episodeStill.OwnerScope);
        Assert.True(File.Exists(seasonPoster.LocalImagePath));
        Assert.True(File.Exists(seasonThumb.LocalImagePath));
        Assert.True(File.Exists(episodeStill.LocalImagePath));
    }

    private ImageEnrichmentService CreateService(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        return new ImageEnrichmentService(
            _entityAssets,
            _mediaAssets,
            new StubCharacterPortraitRepository(),
            _canonicals,
            _works,
            new StubFictionalEntityRepository(),
            new StubPersonRepository(),
            new StubProviderConfigurationRepository(),
            _configLoader,
            _imageCache,
            _imagePaths,
            _assetPaths,
            new StubAssetExportService(),
            new RoutingHttpClientFactory(responder),
            new StubFuzzyMatchingService(),
            NullLogger<ImageEnrichmentService>.Instance);
    }

    private async Task<(Guid WorkId, Guid AssetId)> SeedStandaloneAssetAsync(
        MediaType mediaType,
        string mediaTypeFolder,
        string canonicalMediaType,
        string relativeFilePath)
    {
        var workId = await _works.InsertStandaloneAsync(mediaType);
        var asset = await SeedAssetForExistingWorkAsync(workId, Path.Combine(mediaTypeFolder, relativeFilePath));
        await SeedCanonicalsAsync(workId, ("media_type", canonicalMediaType));
        return (workId, asset.AssetId);
    }

    private async Task<(Guid EditionId, Guid AssetId, string FilePath)> SeedAssetForExistingWorkAsync(Guid workId, string relativeFilePath)
    {
        var editionId = Guid.NewGuid();
        var filePath = Path.Combine(_libraryRoot, relativeFilePath);
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        await File.WriteAllBytesAsync(filePath, [0, 1, 2]);

        using var conn = _db.CreateConnection();
        await conn.ExecuteAsync(
            "INSERT INTO editions (id, work_id) VALUES (@EditionId, @WorkId);",
            new
            {
                EditionId = editionId.ToString(),
                WorkId = workId.ToString(),
            });

        var assetId = Guid.NewGuid();
        await _mediaAssets.InsertAsync(new MediaAsset
        {
            Id = assetId,
            EditionId = editionId,
            ContentHash = $"hash_{assetId:N}",
            FilePathRoot = filePath,
            Status = AssetStatus.Normal,
        });

        return (editionId, assetId, filePath);
    }

    private Task SeedCanonicalsAsync(Guid entityId, params (string Key, string Value)[] values)
    {
        return _canonicals.UpsertBatchAsync(values
            .Select(value => new CanonicalValue
            {
                EntityId = entityId,
                Key = value.Key,
                Value = value.Value,
                LastScoredAt = DateTimeOffset.UtcNow,
            })
            .ToList());
    }

    private static HttpResponseMessage JsonResponse(string payload) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };

    private static HttpResponseMessage ImageResponse(byte[] bytes) =>
        new(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(bytes),
        };

    private sealed class RoutingHttpClientFactory : IHttpClientFactory
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public RoutingHttpClientFactory(Func<HttpRequestMessage, HttpResponseMessage> responder)
            => _responder = responder;

        public HttpClient CreateClient(string name)
            => new(new RoutingHttpMessageHandler(_responder), disposeHandler: true);
    }

    private sealed class RoutingHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public RoutingHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
            => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_responder(request));
    }

    private sealed class StubProviderConfigurationRepository : IProviderConfigurationRepository
    {
        public Task<IReadOnlyList<MediaEngine.Domain.Entities.ProviderConfiguration>> GetAllMaskedAsync(string providerId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<MediaEngine.Domain.Entities.ProviderConfiguration>>([]);

        public Task<string?> GetDecryptedValueAsync(string providerId, string key, CancellationToken ct = default)
            => Task.FromResult<string?>(null);

        public Task UpsertAsync(string providerId, string key, string plaintextValue, bool isSecret, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task DeleteAsync(string providerId, string key, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class StubCharacterPortraitRepository : ICharacterPortraitRepository
    {
        public Task<CharacterPortrait?> FindByIdAsync(Guid portraitId, CancellationToken ct = default)
            => Task.FromResult<CharacterPortrait?>(null);

        public Task<IReadOnlyList<CharacterPortrait>> GetByCharacterAsync(Guid fictionalEntityId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<CharacterPortrait>>([]);

        public Task<IReadOnlyList<CharacterPortrait>> GetByPersonAsync(Guid personId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<CharacterPortrait>>([]);

        public Task<CharacterPortrait?> GetDefaultAsync(Guid fictionalEntityId, CancellationToken ct = default)
            => Task.FromResult<CharacterPortrait?>(null);

        public Task UpsertAsync(CharacterPortrait portrait, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task SetDefaultAsync(Guid portraitId, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<CharacterPortrait>> GetByCharacterBatchAsync(IEnumerable<Guid> fictionalEntityIds, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<CharacterPortrait>>([]);
    }

    private sealed class StubFictionalEntityRepository : IFictionalEntityRepository
    {
        public Task<FictionalEntity?> FindByQidAsync(string qid, CancellationToken ct = default) => Task.FromResult<FictionalEntity?>(null);
        public Task<FictionalEntity?> FindByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult<FictionalEntity?>(null);
        public Task<IReadOnlyList<FictionalEntity>> GetByUniverseAsync(string universeQid, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<FictionalEntity>>([]);
        public Task<IReadOnlyList<FictionalEntity>> GetByUniverseAndTypeAsync(string universeQid, string entitySubType, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<FictionalEntity>>([]);
        public Task<IReadOnlyList<FictionalEntity>> GetByWorkQidAsync(string workQid, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<FictionalEntity>>([]);
        public Task CreateAsync(FictionalEntity entity, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateEnrichmentAsync(Guid entityId, string? description, string? imageUrl, DateTimeOffset enrichedAt, CancellationToken ct = default) => Task.CompletedTask;
        public Task LinkToWorkAsync(Guid entityId, string workQid, string? workLabel, string linkType = "appears_in", CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<(string WorkQid, string? WorkLabel, string LinkType)>> GetWorkLinksAsync(Guid entityId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<(string WorkQid, string? WorkLabel, string LinkType)>>([]);
        public Task<int> CountAsync(CancellationToken ct = default) => Task.FromResult(0);
        public Task UpdateRevisionAsync(Guid entityId, long revisionId, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<FictionalEntity>> GetStaleEntitiesAsync(int staleAfterDays, int limit, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<FictionalEntity>>([]);
    }

    private sealed class StubPersonRepository : IPersonRepository
    {
        public Task<Person?> FindByNameAsync(string name, CancellationToken ct = default) => Task.FromResult<Person?>(null);
        public Task AddRoleAsync(Guid personId, string role, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<string>> GetRolesAsync(Guid personId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<Dictionary<string, int>> GetRoleCountsAsync(CancellationToken ct = default) => Task.FromResult(new Dictionary<string, int>());
        public Task<Dictionary<Guid, Dictionary<string, int>>> GetPresenceBatchAsync(IEnumerable<Guid> personIds, CancellationToken ct = default) => Task.FromResult(new Dictionary<Guid, Dictionary<string, int>>());
        public Task<Person> CreateAsync(Person person, CancellationToken ct = default) => Task.FromResult(person);
        public Task UpdateEnrichmentAsync(Guid personId, string? wikidataQid, string? headshotUrl, string? biography, string? name, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateSocialFieldsAsync(Guid personId, string? occupation, string? instagram, string? twitter, string? tiktok, string? mastodon, string? website, CancellationToken ct = default) => Task.CompletedTask;
        public Task LinkToMediaAssetAsync(Guid mediaAssetId, Guid personId, string role, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateLocalHeadshotPathAsync(Guid id, string path, CancellationToken ct = default) => Task.CompletedTask;
        public Task<Person?> FindByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult<Person?>(null);
        public Task<IReadOnlyList<Person>> GetByMediaAssetAsync(Guid mediaAssetId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Person>>([]);
        public Task<IReadOnlyList<Person>> ListAllAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Person>>([]);
        public Task<int> CountMediaLinksAsync(Guid personId, CancellationToken ct = default) => Task.FromResult(0);
        public Task<Person?> FindByQidAsync(string qid, CancellationToken ct = default) => Task.FromResult<Person?>(null);
        public Task DeleteAsync(Guid personId, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateBiographicalFieldsAsync(Guid personId, string? dateOfBirth, string? dateOfDeath, string? placeOfBirth, string? placeOfDeath, string? nationality, bool isPseudonym, bool isGroup = false, CancellationToken ct = default) => Task.CompletedTask;
        public Task LinkAliasAsync(Guid pseudonymPersonId, Guid realPersonId, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<Person>> FindAliasesAsync(Guid personId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Person>>([]);
        public Task LinkToCharacterAsync(Guid personId, Guid fictionalEntityId, string? workQid, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<(Guid FictionalEntityId, string? WorkQid)>> GetCharacterLinksAsync(Guid personId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<(Guid FictionalEntityId, string? WorkQid)>>([]);
        public Task<IReadOnlyList<(Guid PersonId, Guid FictionalEntityId)>> GetCharacterLinksByWorkAsync(string workQid, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<(Guid PersonId, Guid FictionalEntityId)>>([]);
        public Task ReassignAllLinksAsync(Guid fromPersonId, Guid toPersonId, CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> IsPseudonymOrAliasAsync(Guid personId, CancellationToken ct = default) => Task.FromResult(false);
        public Task LinkGroupMemberAsync(Guid groupId, Guid memberId, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class StubAssetExportService : IAssetExportService
    {
        public Task ReconcileAllArtworkAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task ReconcileArtworkAsync(string entityId, string entityType, string assetType, CancellationToken ct = default) => Task.CompletedTask;
        public Task ClearArtworkExportAsync(string entityId, string entityType, string assetType, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class StubFuzzyMatchingService : IFuzzyMatchingService
    {
        public double ComputeTokenSetRatio(string a, string b) => 0d;
        public double ComputePartialRatio(string a, string b) => 0d;
        public FieldMatchResult ScoreCandidate(LocalMetadata local, CandidateMetadata candidate) => new();
    }
}
