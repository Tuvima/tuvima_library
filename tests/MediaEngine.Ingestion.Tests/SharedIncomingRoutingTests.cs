using MediaEngine.Domain.Configuration;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Enums;
using MediaEngine.Ingestion.Models;
using MediaEngine.Ingestion.Services;
using MediaEngine.Ingestion.Tests.Helpers;
using MediaEngine.Storage.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MediaEngine.Ingestion.Tests;

public sealed class SharedIncomingRoutingTests
{
    [Fact]
    public void ResolveIncomingIntakeContext_UsesStableConfiguredSourceIdAndPathContainment()
    {
        var incomingRoot = Path.Combine(Path.GetTempPath(), "tuvima-incoming");
        var options = new IngestionOptions
        {
            IncomingSources =
            [
                new IncomingSourceEntry
                {
                    Id = "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa",
                    Path = incomingRoot,
                },
            ],
        };

        var intake = options.ResolveIncomingIntakeContext(
            Path.Combine(incomingRoot, "nested", "movie.mkv"));

        Assert.NotNull(intake);
        Assert.Equal(IntakeSourceKinds.SharedIncoming, intake.SourceKind);
        Assert.Equal("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa", intake.SourceId);
        Assert.Null(intake.DestinationLibraryId);
        Assert.Null(options.ResolveIncomingIntakeContext(incomingRoot + "-other/movie.mkv"));
    }

    [Fact]
    public void Route_SelectsOnlyLibraryAcceptingIncomingFolderAndDetectedMediaType()
    {
        var movies = Library("movies", "Movies", MediaType.Movies, acceptsIncoming: true);
        var music = Library("music", "Music", MediaType.Music, acceptsIncoming: true);

        var result = SharedIncomingRouter.Route(
            SharedIntake(),
            MediaType.Movies,
            [movies, music]);

        Assert.True(result.IsResolved);
        Assert.Same(movies, result.Library);
        Assert.False(result.UsedExplicitHint);
    }

    [Fact]
    public void Route_DoesNotGuessWhenNoLibraryIsEligible()
    {
        var result = SharedIncomingRouter.Route(
            SharedIntake(),
            MediaType.Movies,
            [Library("movies", "Movies", MediaType.Movies, acceptsIncoming: false)]);

        Assert.True(result.Applies);
        Assert.False(result.IsResolved);
        Assert.Contains("No library accepts", result.FailureReason);
    }

    [Fact]
    public void Route_DoesNotGuessWhenMultipleLibrariesAreEligible()
    {
        var result = SharedIncomingRouter.Route(
            SharedIntake(),
            MediaType.Movies,
            [
                Library("movies-one", "Movies One", MediaType.Movies, acceptsIncoming: true),
                Library("movies-two", "Movies Two", MediaType.Movies, acceptsIncoming: true),
            ]);

        Assert.True(result.Applies);
        Assert.False(result.IsResolved);
        Assert.Contains("Multiple libraries", result.FailureReason);
        Assert.Contains("explicit destination", result.FailureReason);
    }

    [Fact]
    public void Route_PreservesExplicitDestinationHint()
    {
        var hinted = Library("explicit", "Explicit", MediaType.Music, acceptsIncoming: false);
        var intake = SharedIntake() with { DestinationLibraryId = hinted.Id };

        var result = SharedIncomingRouter.Route(intake, MediaType.Movies, [hinted]);

        Assert.True(result.IsResolved);
        Assert.Same(hinted, result.Library);
        Assert.True(result.UsedExplicitHint);
    }

    [Fact]
    public void Route_TreatsMixedPersonalViewAsEligibleButParksItOutsideCatalogue()
    {
        var personal = Library(
            "personal",
            "Family Archive",
            MediaType.Unknown,
            acceptsIncoming: true,
            kind: LibraryKinds.Personal,
            metadataPolicy: LibraryMetadataPolicies.LocalOnly,
            area: LibraryAreas.View,
            mediaTypes: []);

        var result = SharedIncomingRouter.Route(SharedIntake(), MediaType.Movies, [personal]);

        Assert.True(result.Applies);
        Assert.False(result.IsResolved);
        Assert.Contains("shared incoming-to-View indexing is not available", result.FailureReason);
        Assert.Contains("not sent through catalogue providers", result.FailureReason);
    }

    [Fact]
    public void Route_ExplicitPersonalViewHintRemainsUnsupportedOutsideLocalAssetIntake()
    {
        var personal = Library(
            "personal",
            "Phone Photos",
            MediaType.Unknown,
            acceptsIncoming: true,
            kind: LibraryKinds.Personal,
            metadataPolicy: LibraryMetadataPolicies.LocalOnly,
            area: LibraryAreas.View,
            mediaTypes: []);

        var result = SharedIncomingRouter.Route(
            SharedIntake() with { DestinationLibraryId = personal.Id },
            MediaType.Movies,
            [personal]);

        Assert.False(result.IsResolved);
        Assert.Contains("Personal View library", result.FailureReason);
    }

    [Fact]
    public void IncomingMutationPolicy_BlocksIndexInPlaceAndAllowsRoutedIntakeMoves()
    {
        var indexInPlace = FileSourceMutationPolicyFactory.Create(new IncomingSourceEntry
        {
            Id = "index",
            Path = Path.Combine(Path.GetTempPath(), "index"),
            DefaultHandling = IncomingDefaultHandling.IndexInPlace,
        });
        var routed = FileSourceMutationPolicyFactory.Create(new IncomingSourceEntry
        {
            Id = "routed",
            Path = Path.Combine(Path.GetTempPath(), "routed"),
            DefaultHandling = IncomingDefaultHandling.RouteAutomatically,
        });

        Assert.False(indexInPlace.AllowMove);
        Assert.False(indexInPlace.AllowDelete);
        Assert.True(routed.AllowMove);
    }

    [Fact]
    public async Task InitialSweep_IncludesTopLevelIncomingSources()
    {
        var root = Path.Combine(Path.GetTempPath(), $"tuvima-incoming-sweep-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        await File.WriteAllBytesAsync(Path.Combine(root, "movie.mp4"), [1, 2, 3, 4]);

        try
        {
            var cache = new RecordingHashCache();
            var service = new InitialSweepService(
                new TestAssetHasher(),
                cache,
                new StubEventPublisher(),
                Options.Create(new IngestionOptions
                {
                    IncomingSources =
                    [
                        new IncomingSourceEntry
                        {
                            Id = "incoming",
                            Path = root,
                        },
                    ],
                }),
                new MediaTypeExtensionCatalog(new StubConfigurationLoader()),
                NullLogger<InitialSweepService>.Instance);

            var result = await service.RunAsync();

            Assert.Equal(1, result.FilesDiscovered);
            Assert.Equal(1, result.FilesHashed);
            Assert.Single(cache.UpsertedPaths);
            Assert.StartsWith(root, cache.UpsertedPaths[0], StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static IntakeContext SharedIntake() => new()
    {
        SourceKind = IntakeSourceKinds.SharedIncoming,
        SourceId = "incoming",
    };

    private static LibraryFolderEntry Library(
        string id,
        string name,
        MediaType mediaType,
        bool acceptsIncoming,
        string kind = LibraryKinds.Catalogued,
        string metadataPolicy = LibraryMetadataPolicies.Enriched,
        string area = LibraryAreas.Read,
        IReadOnlyList<MediaType>? mediaTypes = null) => new()
    {
        Id = id,
        Name = name,
        Kind = kind,
        Area = area,
        MetadataPolicy = metadataPolicy,
        MediaTypes = mediaTypes ?? [mediaType],
        AcceptedIntakeModes = acceptsIncoming ? [LibraryIntakeModes.IncomingFolder] : [],
    };

    private sealed class RecordingHashCache : IFileHashCacheRepository
    {
        public List<string> UpsertedPaths { get; } = [];

        public Task<string?> TryGetAsync(
            string absolutePath,
            long sizeBytes,
            DateTimeOffset mtimeUtc,
            CancellationToken ct = default) => Task.FromResult<string?>(null);

        public Task UpsertAsync(
            string absolutePath,
            long sizeBytes,
            DateTimeOffset mtimeUtc,
            string sha256,
            CancellationToken ct = default)
        {
            UpsertedPaths.Add(absolutePath);
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string absolutePath, CancellationToken ct = default) =>
            Task.CompletedTask;
    }
}
