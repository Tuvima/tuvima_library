using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Models;
using MediaEngine.Ingestion.Models;
using MediaEngine.Ingestion.Services;
using MediaEngine.Ingestion.Tests.Helpers;
using MediaEngine.Processors.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaEngine.Ingestion.Tests;

public sealed class MediaTypeResolverTests
{
    [Fact]
    public async Task LibraryFolderPrior_MatchesEveryConfiguredSourcePath()
    {
        var rootA = TestRoot("books-a");
        var rootB = TestRoot("books-b");
        var options = Options(
            watchDirectories: [rootA, rootB],
            libraryFolders:
            [
                new LibraryFolderEntry
                {
                    SourcePaths = [rootA, rootB],
                    MediaTypes = [MediaType.Audiobooks],
                },
            ]);

        var resolver = CreateResolver(options);
        var filePath = Path.Combine(rootB, "incoming", "incoming.mp3");

        var resolution = await resolver.ResolveAsync(
            filePath,
            Result(MediaType.Unknown,
            [
                Candidate(MediaType.Music, 0.72),
                Candidate(MediaType.Audiobooks, 0.62),
            ]),
            0,
            CancellationToken.None);

        Assert.Equal(MediaType.Audiobooks, resolution.MediaType);
        Assert.False(resolution.NeedsReview);
        Assert.True(resolution.Candidates[0].Confidence >= 0.98);
    }

    [Fact]
    public async Task RootWatchFolderCap_AppliesPerConfiguredRoot_AndSkipsUnambiguousExtensions()
    {
        var rootA = TestRoot("root-a");
        var rootB = TestRoot("root-b");
        var options = Options(watchDirectories: [rootA, rootB]);
        var resolver = CreateResolver(options);

        var ambiguous = await resolver.ResolveAsync(
            Path.Combine(rootB, "clip.mp3"),
            Result(MediaType.Unknown, [Candidate(MediaType.Music, 0.95)]),
            0.1,
            CancellationToken.None);

        Assert.True(ambiguous.RootWatchFolderReview);
        Assert.True(ambiguous.NeedsReview);
        Assert.Equal(MediaType.Unknown, ambiguous.MediaType);
        Assert.Equal(0.0, ambiguous.CategoryConfidencePrior);
        Assert.Equal(0.40, ambiguous.Candidates[0].Confidence, precision: 2);

        var unambiguous = await resolver.ResolveAsync(
            Path.Combine(rootB, "book.epub"),
            Result(MediaType.Books, [Candidate(MediaType.Books, 0.95)]),
            0.1,
            CancellationToken.None);

        Assert.False(unambiguous.RootWatchFolderReview);
        Assert.False(unambiguous.NeedsReview);
        Assert.Equal(MediaType.Books, unambiguous.MediaType);
        Assert.Equal(0.1, unambiguous.CategoryConfidencePrior);
    }

    [Fact]
    public async Task Advisor_IsCalledOnlyForUnknownOrLowConfidenceCases()
    {
        var options = Options(watchDirectories: [Path.Combine(Path.GetTempPath(), "advisor")]);
        var advisor = new CountingAdvisor(Candidate(MediaType.Audiobooks, 0.91));
        var resolver = CreateResolver(options, advisor);

        await resolver.ResolveAsync(
            Path.Combine(options.WatchDirectories[0], "movie.mkv"),
            Result(MediaType.Movies, [Candidate(MediaType.Movies, 0.92)]),
            0,
            CancellationToken.None);

        Assert.Equal(0, advisor.CallCount);

        await resolver.ResolveAsync(
            Path.Combine(options.WatchDirectories[0], "unknown.m4a"),
            Result(MediaType.Unknown, []),
            0,
            CancellationToken.None);

        Assert.Equal(1, advisor.CallCount);
    }

    [Theory]
    [InlineData(0.90, MediaType.Music, false, false)]
    [InlineData(0.70, MediaType.Music, true, true)]
    [InlineData(0.40, MediaType.Unknown, true, true)]
    public async Task Outcomes_FollowConfiguredThresholds(
        double confidence,
        MediaType expectedType,
        bool expectedConflict,
        bool expectedReview)
    {
        var root = Path.Combine(TestRoot("thresholds"), "nested");
        var options = Options(watchDirectories: [TestRoot("threshold-watch")]);
        var resolver = CreateResolver(options);

        var resolution = await resolver.ResolveAsync(
            Path.Combine(root, "track.mp3"),
            Result(MediaType.Unknown, [Candidate(MediaType.Music, confidence)]),
            0,
            CancellationToken.None);

        Assert.Equal(expectedType, resolution.MediaType);
        Assert.Equal(expectedConflict, resolution.IsConflicted);
        Assert.Equal(expectedReview, resolution.NeedsReview);
    }

    private static MediaTypeResolver CreateResolver(
        IngestionOptions options,
        IMediaTypeAdvisor? advisor = null)
    {
        var monitor = new OptionsMonitorStub<IngestionOptions>(options);
        return new MediaTypeResolver(
            monitor,
            new LibraryFolderResolver(monitor),
            advisor ?? new CountingAdvisor(Candidate(MediaType.Unknown, 0)),
            NullLogger<MediaTypeResolver>.Instance);
    }

    private static IngestionOptions Options(
        IReadOnlyList<string> watchDirectories,
        IReadOnlyList<LibraryFolderEntry>? libraryFolders = null) => new()
    {
        WatchDirectories = watchDirectories,
        LibraryFolders = libraryFolders ?? [],
        MediaTypeAutoAssignThreshold = 0.85,
        MediaTypeReviewThreshold = 0.50,
    };

    private static ProcessorResult Result(
        MediaType detectedType,
        IReadOnlyList<MediaTypeCandidate> candidates) => new()
    {
        FilePath = "test",
        DetectedType = detectedType,
        MediaTypeCandidates = candidates,
    };

    private static MediaTypeCandidate Candidate(MediaType type, double confidence) => new()
    {
        Type = type,
        Confidence = confidence,
        Reason = "test",
    };

    private static string TestRoot(string name) =>
        Path.Combine(Path.GetTempPath(), "tuvima-media-type-resolver-tests", name);

    private sealed class CountingAdvisor : IMediaTypeAdvisor
    {
        private readonly MediaTypeCandidate _candidate;

        public CountingAdvisor(MediaTypeCandidate candidate)
            => _candidate = candidate;

        public int CallCount { get; private set; }

        public Task<MediaTypeCandidate> ClassifyAsync(
            string filename,
            string? container,
            double? durationSeconds,
            int? bitrate,
            string? genre,
            bool hasChapters,
            string? folderPath,
            CancellationToken ct = default)
        {
            CallCount++;
            return Task.FromResult(_candidate);
        }
    }
}
