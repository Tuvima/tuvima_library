using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Models;
using MediaEngine.Ingestion.Contracts;
using MediaEngine.Ingestion.Models;
using MediaEngine.Processors.Contracts;
using MediaEngine.Processors.Models;
using MediaEngine.Storage.Contracts;
using MediaEngine.Storage.Models;

namespace MediaEngine.Ingestion.Tests.Helpers;

// ── Event Publisher Stub ─────────────────────────────────────────────────────

internal sealed class StubEventPublisher : IEventPublisher
{
    public List<(string EventName, object Payload)> Published { get; } = [];

    public Task PublishAsync<TPayload>(string eventName, TPayload payload, CancellationToken ct = default)
        where TPayload : notnull
    {
        Published.Add((eventName, payload));
        return Task.CompletedTask;
    }
}

// ── File Watcher Stub ─────────────────────────────────────────────────────────

internal sealed class StubFileWatcher : IFileWatcher
{
    public event EventHandler<FileEvent>? FileDetected;
    public long EventCount => 0;
    public DateTimeOffset? LastEventAt => null;
    public bool IsRunning => false;
    public IReadOnlyList<string> WatchedPaths => [];

    public void AddDirectory(string path, bool includeSubdirectories = true) { }
    public void Start() { }
    public void Stop() { }
    public void UpdateDirectory(string path, bool includeSubdirectories = true) { }
    public void Dispose() { }

    // Suppress unused event warning.
    internal void RaiseForTest(FileEvent evt) => FileDetected?.Invoke(this, evt);
}

// ── Background Worker Stub ────────────────────────────────────────────────────

internal sealed class InlineBackgroundWorker : IBackgroundWorker
{
    public int PendingCount => 0;

    public async ValueTask EnqueueAsync<T>(
        T workItem, Func<T, CancellationToken, Task> handler, CancellationToken ct = default)
    {
        // Execute synchronously for test determinism.
        await handler(workItem, ct);
    }

    public Task DrainAsync(CancellationToken ct = default) => Task.CompletedTask;
}

// ── Hydration Pipeline Stub ───────────────────────────────────────────────────

internal sealed class StubHydrationPipeline : IHydrationPipelineService
{
    public List<HarvestRequest> EnqueuedRequests { get; } = [];
    public int PendingCount => 0;

    public ValueTask EnqueueAsync(HarvestRequest request, CancellationToken ct = default)
    {
        EnqueuedRequests.Add(request);
        return ValueTask.CompletedTask;
    }

    public Task<HydrationResult> RunSynchronousAsync(HarvestRequest request, CancellationToken ct = default)
        => Task.FromResult(new HydrationResult());
}

// ── Recursive Identity Stub ───────────────────────────────────────────────────

internal sealed class StubRecursiveIdentity : IRecursiveIdentityService
{
    public List<(Guid AssetId, IReadOnlyList<PersonReference> Persons)> Calls { get; } = [];

    public Task EnrichAsync(Guid mediaAssetId, IReadOnlyList<PersonReference> persons, CancellationToken ct = default)
    {
        Calls.Add((mediaAssetId, persons));
        return Task.CompletedTask;
    }
}

// ── Hero Banner Generator Stub ────────────────────────────────────────────────

internal sealed class StubHeroBannerGenerator : IHeroBannerGenerator
{
    public Task<HeroBannerResult> GenerateAsync(string coverImagePath, string outputDirectory, CancellationToken ct = default)
        => Task.FromResult(new HeroBannerResult("hero.jpg", "#000000", false));
}

// ── Reconciliation Stub ───────────────────────────────────────────────────────

internal sealed class StubReconciliation : IReconciliationService
{
    public Task<ReconciliationSummary> ReconcileAsync(CancellationToken ct = default)
        => Task.FromResult(new ReconciliationSummary(0, 0, 0));
}

// ── File Organizer Stub ───────────────────────────────────────────────────────

internal sealed class StubFileOrganizer : IFileOrganizer
{
    public string CalculatePath(IngestionCandidate candidate, string template)
    {
        var metadata = candidate.Metadata;
        var category = candidate.DetectedMediaType switch
        {
            MediaType.Books => "Books",
            MediaType.Audiobooks => "Audio",
            MediaType.Movies => "Movies",
            MediaType.Comic => "Comics",
            _ => "Other",
        };
        var title = metadata?.GetValueOrDefault("title") ?? "Unknown";
        var ext = Path.GetExtension(candidate.Path);
        return $"{category}/{title}{ext}";
    }

    public string? ValidateTemplate(string template, out string? error)
    {
        error = null;
        return template;
    }

    public Task<bool> ExecuteMoveAsync(string sourcePath, string destinationPath, CancellationToken ct = default)
    {
        var dir = Path.GetDirectoryName(destinationPath);
        if (dir is not null) Directory.CreateDirectory(dir);
        if (File.Exists(sourcePath))
            File.Move(sourcePath, destinationPath, overwrite: true);
        return Task.FromResult(true);
    }
}

// ── Asset Hasher (test-friendly) ──────────────────────────────────────────────

internal sealed class TestAssetHasher : IAssetHasher
{
    public Task<HashResult> ComputeAsync(string filePath, CancellationToken ct = default)
    {
        using var stream = File.OpenRead(filePath);
        var hash = System.Security.Cryptography.SHA256.HashData(stream);
        var hex = Convert.ToHexStringLower(hash);
        return Task.FromResult(new HashResult
        {
            FilePath = filePath,
            Hex = hex,
            FileSize = new FileInfo(filePath).Length,
            Elapsed = TimeSpan.FromMilliseconds(1),
        });
    }
}

// ── Processor Registry (configurable) ─────────────────────────────────────────

internal sealed class TestProcessorRegistry : IProcessorRegistry
{
    private ProcessorResult? _nextResult;
    private readonly Queue<ProcessorResult> _resultQueue = new();

    public void SetNextResult(ProcessorResult result) => _nextResult = result;

    /// <summary>
    /// Enqueues a result to be returned by <see cref="ProcessAsync"/> in FIFO order.
    /// Queue is checked before <see cref="_nextResult"/> and the default fallback.
    /// </summary>
    public void QueueResult(ProcessorResult result) => _resultQueue.Enqueue(result);

    public void Register(IMediaProcessor processor) { }
    public IMediaProcessor? Resolve(string filePath) => null;

    public Task<ProcessorResult> ProcessAsync(string filePath, CancellationToken ct = default)
    {
        // Priority 1: dequeue from FIFO queue.
        if (_resultQueue.TryDequeue(out var queued))
            return Task.FromResult(queued);

        // Priority 2: single-shot next result.
        if (_nextResult is not null)
        {
            var result = _nextResult;
            _nextResult = null;
            return Task.FromResult(result);
        }

        // Default: return minimal valid result for an EPUB-like file.
        return Task.FromResult(new ProcessorResult
        {
            FilePath = filePath,
            DetectedType = MediaType.Books,
            Claims =
            [
                new ExtractedClaim { Key = "title", Value = Path.GetFileNameWithoutExtension(filePath), Confidence = 0.5 },
            ],
        });
    }
}

// ── Configuration Loader Stub ───────────────────────────────────────────────

internal sealed class StubConfigurationLoader : IConfigurationLoader
{
    public CoreConfiguration LoadCore() => new();
    public void SaveCore(CoreConfiguration config) { }
    public ScoringSettings LoadScoring() => new();
    public void SaveScoring(ScoringSettings settings) { }
    public MaintenanceSettings LoadMaintenance() => new();
    public void SaveMaintenance(MaintenanceSettings settings) { }
    public HydrationSettings LoadHydration() => new();
    public void SaveHydration(HydrationSettings settings) { }
    public ProviderSlotConfiguration LoadSlots() => new();
    public void SaveSlots(ProviderSlotConfiguration slots) { }
    public DisambiguationSettings LoadDisambiguation() => new();
    public void SaveDisambiguation(DisambiguationSettings settings) { }
    public MediaTypeConfiguration LoadMediaTypes() => new();
    public void SaveMediaTypes(MediaTypeConfiguration config) { }
    public TranscodingSettings LoadTranscoding() => new();
    public void SaveTranscoding(TranscodingSettings settings) { }
    public FieldPriorityConfiguration LoadFieldPriorities() => new();
    public void SaveFieldPriorities(FieldPriorityConfiguration config) { }
    public LibrariesConfiguration LoadLibraries() => new();
    public ProviderConfiguration? LoadProvider(string name) => null;
    public void SaveProvider(ProviderConfiguration config) { }
    public IReadOnlyList<ProviderConfiguration> LoadAllProviders() => [];
    public T? LoadConfig<T>(string subdirectory, string name) where T : class => null;
    public void SaveConfig<T>(string subdirectory, string name, T config) where T : class { }
}
