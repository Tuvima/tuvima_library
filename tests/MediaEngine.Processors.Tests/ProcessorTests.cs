using MediaEngine.Domain.Enums;
using MediaEngine.Processors;
using MediaEngine.Processors.Contracts;
using MediaEngine.Processors.Models;
using MediaEngine.Processors.Processors;

namespace MediaEngine.Processors.Tests;

// ════════════════════════════════════════════════════════════════════════
//  GenericFileProcessor
// ════════════════════════════════════════════════════════════════════════

public class GenericFileProcessorTests
{
    private readonly GenericFileProcessor _processor = new();

    [Fact]
    public void SupportedType_IsUnknown()
    {
        Assert.Equal(MediaType.Unknown, _processor.SupportedType);
    }

    [Fact]
    public void Priority_IsMinValue()
    {
        Assert.Equal(int.MinValue, _processor.Priority);
    }

    [Fact]
    public void CanProcess_AlwaysReturnsTrue()
    {
        Assert.True(_processor.CanProcess("/any/file.xyz"));
        Assert.True(_processor.CanProcess("test.epub"));
    }

    [Fact]
    public async Task ProcessAsync_ExtractsTitleFromFilenameStem()
    {
        // Create a temp file so the processor can open it.
        // Must use a non-known extension (.xyz) so GenericFileProcessor does not
        // classify the file as corrupt (it marks .epub, .mp4, etc. as corrupt when
        // no format-specific processor could parse them).
        var tempFile = Path.Combine(Path.GetTempPath(), $"My Book Title_{Guid.NewGuid():N}.xyz");
        await File.WriteAllBytesAsync(tempFile, [0x00]);

        try
        {
            var result = await _processor.ProcessAsync(tempFile);

            Assert.Equal(tempFile, result.FilePath);
            Assert.Equal(MediaType.Unknown, result.DetectedType);
            Assert.Single(result.Claims);
            Assert.Equal("title", result.Claims[0].Key);
            Assert.StartsWith("My Book Title_", result.Claims[0].Value);
            Assert.Equal(0.5, result.Claims[0].Confidence);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ProcessAsync_ThrowsOnNullPath()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _processor.ProcessAsync(null!));
    }

    [Fact]
    public async Task ProcessAsync_ThrowsOnWhitespacePath()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _processor.ProcessAsync("   "));
    }
}

// ════════════════════════════════════════════════════════════════════════
//  MediaProcessorRegistry
// ════════════════════════════════════════════════════════════════════════

public class MediaProcessorRegistryTests : IDisposable
{
    private readonly MediaProcessorRegistry _registry = new(maxDegreeOfParallelism: 2);

    public void Dispose() => _registry.Dispose();

    [Fact]
    public void Register_NullProcessor_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => _registry.Register(null!));
    }

    [Fact]
    public void Resolve_NullPath_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => _registry.Resolve(null!));
    }

    [Fact]
    public void Resolve_EmptyPath_Throws()
    {
        Assert.Throws<ArgumentException>(() => _registry.Resolve("  "));
    }

    [Fact]
    public void Resolve_FallbackProcessor_ReturnedWhenNoSpecificMatch()
    {
        var fallback = new GenericFileProcessor();
        _registry.Register(fallback);

        var result = _registry.Resolve("/some/unknown/file.xyz");

        Assert.Same(fallback, result);
    }

    [Fact]
    public void Resolve_HigherPriorityProcessor_SelectedFirst()
    {
        var lowPriority = new FakeProcessor("low", 10, canProcess: true);
        var highPriority = new FakeProcessor("high", 100, canProcess: true);

        _registry.Register(lowPriority);
        _registry.Register(highPriority);

        var result = _registry.Resolve("/test/file.txt");

        Assert.Same(highPriority, result);
    }

    [Fact]
    public void Resolve_SkipsProcessorsThatCannotProcess()
    {
        var cannotProcess = new FakeProcessor("no", 100, canProcess: false);
        var canProcess = new FakeProcessor("yes", 50, canProcess: true);

        _registry.Register(cannotProcess);
        _registry.Register(canProcess);

        var result = _registry.Resolve("/test/file.txt");

        Assert.Same(canProcess, result);
    }

    [Fact]
    public async Task ProcessAsync_NoProcessors_ThrowsInvalidOperation()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _registry.ProcessAsync("/some/file.txt"));
    }

    [Fact]
    public async Task ProcessAsync_DelegatesToResolvedProcessor()
    {
        var expectedResult = new ProcessorResult
        {
            FilePath = "/test.txt",
            DetectedType = MediaType.Books,
        };
        var processor = new FakeProcessor("test", 100, canProcess: true, result: expectedResult);
        _registry.Register(processor);

        var result = await _registry.ProcessAsync("/test.txt");

        Assert.Same(expectedResult, result);
    }

    [Fact]
    public async Task ProcessAsync_ConcurrencyLimitedBySemaphore()
    {
        // Register a slow processor
        var tcs = new TaskCompletionSource<ProcessorResult>();
        var slowResult = new ProcessorResult { FilePath = "/slow.txt", DetectedType = MediaType.Unknown };
        var slowProcessor = new SlowProcessor(tcs.Task);
        _registry.Register(slowProcessor);

        // Start two tasks (fill the semaphore limit of 2)
        var task1 = _registry.ProcessAsync("/slow.txt");
        var task2 = _registry.ProcessAsync("/slow.txt");

        // Both should be in progress
        Assert.False(task1.IsCompleted);
        Assert.False(task2.IsCompleted);

        // Complete them
        tcs.SetResult(slowResult);
        var result1 = await task1;
        var result2 = await task2;

        Assert.Equal(MediaType.Unknown, result1.DetectedType);
    }
}

// ════════════════════════════════════════════════════════════════════════
//  ProcessorResult model
// ════════════════════════════════════════════════════════════════════════

public class ProcessorResultTests
{
    [Fact]
    public void Default_IsNotCorrupt()
    {
        var result = new ProcessorResult
        {
            FilePath = "/test.epub",
            DetectedType = MediaType.Books,
        };

        Assert.False(result.IsCorrupt);
        Assert.Null(result.CorruptReason);
        Assert.Null(result.CoverImage);
        Assert.Null(result.CoverImageMimeType);
        Assert.Empty(result.Claims);
        Assert.Empty(result.MediaTypeCandidates);
    }

    [Fact]
    public void CorruptResult_HasReason()
    {
        var result = new ProcessorResult
        {
            FilePath = "/bad.epub",
            DetectedType = MediaType.Books,
            IsCorrupt = true,
            CorruptReason = "Truncated ZIP archive",
        };

        Assert.True(result.IsCorrupt);
        Assert.Equal("Truncated ZIP archive", result.CorruptReason);
    }
}

// ── Test doubles ─────────────────────────────────────────────────────────

file sealed class FakeProcessor : IMediaProcessor
{
    private readonly bool _canProcess;
    private readonly ProcessorResult? _result;

    public FakeProcessor(string name, int priority, bool canProcess, ProcessorResult? result = null)
    {
        _canProcess = canProcess;
        _result = result;
        Priority = priority;
    }

    public MediaType SupportedType => MediaType.Unknown;
    public int Priority { get; }
    public bool CanProcess(string filePath) => _canProcess;

    public Task<ProcessorResult> ProcessAsync(string filePath, CancellationToken ct = default)
    {
        return Task.FromResult(_result ?? new ProcessorResult
        {
            FilePath = filePath,
            DetectedType = MediaType.Unknown,
        });
    }
}

file sealed class SlowProcessor : IMediaProcessor
{
    private readonly Task<ProcessorResult> _blocker;

    public SlowProcessor(Task<ProcessorResult> blocker) => _blocker = blocker;

    public MediaType SupportedType => MediaType.Unknown;
    public int Priority => 100;
    public bool CanProcess(string filePath) => true;
    public Task<ProcessorResult> ProcessAsync(string filePath, CancellationToken ct = default) => _blocker;
}
