using System.Collections.Concurrent;
using MediaEngine.Api.Services.LocalAssets;
using MediaEngine.Contracts.LocalAssets;
using MediaEngine.Domain.Configuration;
using MediaEngine.Storage;
using MediaEngine.Storage.Contracts;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaEngine.Api.Tests;

public sealed class ViewSourceIndexingHostedServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"tuvima-view-watch-{Guid.NewGuid():N}");
    private readonly ConfigurationDirectoryLoader _configuration;

    public ViewSourceIndexingHostedServiceTests()
    {
        Directory.CreateDirectory(_root);
        _configuration = new ConfigurationDirectoryLoader(Path.Combine(_root, "config"));
    }

    [Fact]
    public void ConfigurationSelectionAndPathResolutionFailClosed()
    {
        var personalRoot = Directory.CreateDirectory(Path.Combine(_root, "personal")).FullName;
        var nestedRoot = Directory.CreateDirectory(Path.Combine(personalRoot, "private")).FullName;
        var catalogueRoot = Directory.CreateDirectory(Path.Combine(_root, "catalogue")).FullName;
        var personalId = Guid.NewGuid();
        var nestedId = Guid.NewGuid();
        var settings = new LibrariesConfiguration
        {
            Libraries =
            [
                Library(personalId, LibraryKinds.Personal, LibraryAreas.View,
                    LibraryMetadataPolicies.LocalOnly, personalRoot, includeSubdirectories: true),
                Library(nestedId, LibraryKinds.Personal, LibraryAreas.View,
                    LibraryMetadataPolicies.Manual, nestedRoot, includeSubdirectories: false),
                Library(Guid.NewGuid(), LibraryKinds.Catalogued, LibraryAreas.Read,
                    LibraryMetadataPolicies.Enriched, catalogueRoot, includeSubdirectories: true),
                Library(Guid.NewGuid(), LibraryKinds.Personal, LibraryAreas.Watch,
                    LibraryMetadataPolicies.LocalOnly, Path.Combine(_root, "wrong-area"), true),
                Library(Guid.NewGuid(), LibraryKinds.Personal, LibraryAreas.View,
                    LibraryMetadataPolicies.Enriched, Path.Combine(_root, "provider-backed"), true),
                Library(Guid.NewGuid(), LibraryKinds.Personal, LibraryAreas.View,
                    LibraryMetadataPolicies.LocalOnly, Path.Combine(_root, "network"), true,
                    LibrarySourceTypes.NetworkShare),
                Library(Guid.NewGuid(), LibraryKinds.Personal, LibraryAreas.View,
                    LibraryMetadataPolicies.LocalOnly, Path.Combine(_root, "unsupported-source"), true,
                    "cloud_api"),
            ],
        };

        var sources = ViewSourceIndexingHostedService.SelectConfiguredSources(settings);

        Assert.Equal(3, sources.Count);
        Assert.Contains(sources, source => source.RootPath.EndsWith("network", StringComparison.OrdinalIgnoreCase));
        Assert.True(ViewSourceIndexingHostedService.TryResolveLibrary(
            sources, Path.Combine(personalRoot, "photo.jpg"), out var rootLibrary));
        Assert.Equal(personalId, rootLibrary);
        Assert.True(ViewSourceIndexingHostedService.TryResolveLibrary(
            sources, Path.Combine(nestedRoot, "private.jpg"), out var nestedLibrary));
        Assert.Equal(nestedId, nestedLibrary);
        Assert.False(ViewSourceIndexingHostedService.TryResolveLibrary(
            sources, Path.Combine(nestedRoot, "child", "blocked.jpg"), out _));
        Assert.False(ViewSourceIndexingHostedService.TryResolveLibrary(
            sources, Path.Combine(_root, "personal-escape", "outside.jpg"), out _));

        var ambiguous = new[]
        {
            new ViewSourceWatch(personalId, personalRoot, true),
            new ViewSourceWatch(Guid.NewGuid(), personalRoot, true),
        };
        Assert.False(ViewSourceIndexingHostedService.TryResolveLibrary(
            ambiguous, Path.Combine(personalRoot, "ambiguous.jpg"), out _));
    }

    [Fact]
    public async Task StartsNonBlockingReconcilesDebouncesRetriesAndDispatchesOnlySafePaths()
    {
        var personalRoot = Directory.CreateDirectory(Path.Combine(_root, "events")).FullName;
        var libraryId = Guid.NewGuid();
        _configuration.SaveLibraries(new LibrariesConfiguration
        {
            Libraries = [Library(libraryId, LibraryKinds.Personal, LibraryAreas.View,
                LibraryMetadataPolicies.LocalOnly, personalRoot, includeSubdirectories: true)],
        });
        var watcherFactory = new FakeWatcherFactory();
        var indexer = new RecordingIndexer(blockScan: true, failFirstDispatch: true);
        using var service = Service(indexer, watcherFactory);

        await service.StartAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(1));

        await WaitUntilAsync(() => watcherFactory.Watchers.Count == 1);
        var watcher = Assert.Single(watcherFactory.Watchers);
        Assert.True(watcher.Started);
        await indexer.ScanStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Equal([libraryId], indexer.Scans);

        var outside = Path.Combine(_root, "outside.jpg");
        File.WriteAllBytes(outside, [0]);
        watcher.Raise(new ViewSourceFileEvent(outside, ViewSourceFileEventKind.Created));

        var path = Path.Combine(personalRoot, "new-photo.jpg");
        File.WriteAllBytes(path, [1, 2, 3]);
        watcher.Raise(new ViewSourceFileEvent(path, ViewSourceFileEventKind.Created));
        watcher.Raise(new ViewSourceFileEvent(path, ViewSourceFileEventKind.Changed));
        watcher.Raise(new ViewSourceFileEvent(path, ViewSourceFileEventKind.Changed));

        await indexer.FirstSuccessfulDispatch.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await Task.Delay(100);
        Assert.Equal(2, indexer.DispatchAttempts); // one transient failure, then retry
        Assert.Equal((libraryId, Path.GetFullPath(path)), Assert.Single(indexer.SuccessfulDispatches));
        Assert.DoesNotContain(indexer.AttemptedPaths,
            candidate => string.Equals(candidate, outside, StringComparison.OrdinalIgnoreCase));

        var renamed = Path.Combine(personalRoot, "renamed-photo.jpg");
        File.Move(path, renamed);
        watcher.Raise(new ViewSourceFileEvent(renamed, ViewSourceFileEventKind.Renamed, path));
        await WaitUntilAsync(() => indexer.SuccessfulDispatches.Count == 2);
        Assert.Contains((libraryId, Path.GetFullPath(renamed)), indexer.SuccessfulDispatches);

        indexer.ReleaseScan();
        await service.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(watcher.Stopped);
        Assert.True(watcher.Disposed);
    }

    [Fact]
    public async Task StopAndDisposeReleaseEveryWatcherWhenThereAreMultipleSources()
    {
        var first = Directory.CreateDirectory(Path.Combine(_root, "one")).FullName;
        var second = Directory.CreateDirectory(Path.Combine(_root, "two")).FullName;
        _configuration.SaveLibraries(new LibrariesConfiguration
        {
            Libraries =
            [
                Library(Guid.NewGuid(), LibraryKinds.Personal, LibraryAreas.View,
                    LibraryMetadataPolicies.LocalOnly, first, true),
                Library(Guid.NewGuid(), LibraryKinds.Personal, LibraryAreas.View,
                    LibraryMetadataPolicies.LocalOnly, second, true),
            ],
        });
        var factory = new FakeWatcherFactory();
        var indexer = new RecordingIndexer();
        var service = Service(indexer, factory);

        await service.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => factory.Watchers.Count == 2);
        await service.StopAsync(CancellationToken.None);
        service.Dispose();

        Assert.All(factory.Watchers, watcher =>
        {
            Assert.True(watcher.Started);
            Assert.True(watcher.Stopped);
            Assert.True(watcher.Disposed);
        });
    }

    private ViewSourceIndexingHostedService Service(
        IViewPathIndexer indexer,
        IViewSourceWatcherFactory watcherFactory) => new(
            _configuration,
            indexer,
            watcherFactory,
            new ViewSourceIndexingOptions
            {
                SettleDelay = TimeSpan.FromMilliseconds(35),
                ProbeInterval = TimeSpan.FromMilliseconds(10),
                MaxProbeDelay = TimeSpan.FromMilliseconds(25),
                MaxProbeAttempts = 3,
                DispatchAttempts = 3,
                DispatchRetryDelay = TimeSpan.FromMilliseconds(10),
                QueueCapacity = 16,
            },
            NullLogger<ViewSourceIndexingHostedService>.Instance);

    private static LibraryFolderConfig Library(
        Guid id,
        string kind,
        string area,
        string metadataPolicy,
        string root,
        bool includeSubdirectories,
        string sourceType = LibrarySourceTypes.LocalFolder) => new()
    {
        Id = id.ToString("D"),
        Name = id.ToString("N"),
        Kind = kind,
        Area = area,
        Presentation = kind == LibraryKinds.Personal
            ? LibraryPresentations.MixedGallery
            : LibraryPresentations.Catalogue,
        MetadataPolicy = metadataPolicy,
        OwnerProfileId = kind == LibraryKinds.Personal ? Guid.NewGuid().ToString("D") : null,
        Sources =
        [
            new LibrarySourceConfig
            {
                Id = Guid.NewGuid().ToString("D"),
                Path = root,
                SourceType = sourceType,
                IncludeSubdirectories = includeSubdirectories,
            },
        ],
    };

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var timeout = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(3);
        while (!condition())
        {
            if (DateTimeOffset.UtcNow >= timeout) throw new TimeoutException("Expected asynchronous condition was not reached.");
            await Task.Delay(20);
        }
    }

    public void Dispose()
    {
        _configuration.Dispose();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed class FakeWatcherFactory : IViewSourceWatcherFactory
    {
        public ConcurrentQueue<FakeWatcher> Watchers { get; } = [];
        public IViewSourceWatcher Create(ViewSourceWatch source)
        {
            var watcher = new FakeWatcher(source);
            Watchers.Enqueue(watcher);
            return watcher;
        }
    }

    private sealed class FakeWatcher(ViewSourceWatch source) : IViewSourceWatcher
    {
        public ViewSourceWatch Source { get; } = source;
        public bool Started { get; private set; }
        public bool Stopped { get; private set; }
        public bool Disposed { get; private set; }
        public event EventHandler<ViewSourceFileEvent>? FileChanged;
        public event EventHandler<Exception>? WatcherError;
        public void Start() => Started = true;
        public void Stop() => Stopped = true;
        public void Raise(ViewSourceFileEvent change) => FileChanged?.Invoke(this, change);
        public void RaiseError(Exception exception) => WatcherError?.Invoke(this, exception);
        public void Dispose() => Disposed = true;
    }

    private sealed class RecordingIndexer(bool blockScan = false, bool failFirstDispatch = false) : IViewPathIndexer
    {
        private readonly TaskCompletionSource _scanRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _failed;
        public ConcurrentQueue<Guid> Scans { get; } = [];
        public ConcurrentQueue<string> AttemptedPaths { get; } = [];
        public ConcurrentQueue<(Guid LibraryId, string Path)> SuccessfulDispatches { get; } = [];
        public TaskCompletionSource ScanStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource FirstSuccessfulDispatch { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int DispatchAttempts => AttemptedPaths.Count;

        public async Task<LocalAssetScanResultDto?> ScanAsync(Guid libraryId, CancellationToken ct = default)
        {
            Scans.Enqueue(libraryId);
            ScanStarted.TrySetResult();
            if (blockScan) await _scanRelease.Task.WaitAsync(ct);
            return new LocalAssetScanResultDto(libraryId, 0, 0, 0, 0, 0, 0);
        }

        public Task<LocalAssetUpsertResult?> IndexPathAsync(
            Guid libraryId,
            string path,
            CancellationToken ct = default)
        {
            AttemptedPaths.Enqueue(path);
            if (failFirstDispatch && Interlocked.Exchange(ref _failed, 1) == 0)
                throw new IOException("Simulated partial write race.");
            SuccessfulDispatches.Enqueue((libraryId, Path.GetFullPath(path)));
            FirstSuccessfulDispatch.TrySetResult();
            return Task.FromResult<LocalAssetUpsertResult?>(
                new LocalAssetUpsertResult(Guid.NewGuid(), true, 1, 1));
        }

        public void ReleaseScan() => _scanRelease.TrySetResult();
    }
}
