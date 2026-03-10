using MediaEngine.Ingestion;
using MediaEngine.Ingestion.Models;

namespace MediaEngine.Ingestion.Tests;

// ════════════════════════════════════════════════════════════════════════
//  AssetHasher
// ════════════════════════════════════════════════════════════════════════

public class AssetHasherTests : IDisposable
{
    private readonly string _tempDir;
    private readonly AssetHasher _hasher = new();

    public AssetHasherTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"hasher_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public async Task ComputeAsync_ReturnsLowercaseHex64Chars()
    {
        var file = CreateFile("test.bin", "Hello, World!");
        var result = await _hasher.ComputeAsync(file);

        Assert.Equal(64, result.Hex.Length);
        Assert.Equal(result.Hex, result.Hex.ToLowerInvariant());
        Assert.True(result.Hex.All(c => "0123456789abcdef".Contains(c)));
    }

    [Fact]
    public async Task ComputeAsync_SameContent_SameHash()
    {
        var file1 = CreateFile("a.bin", "identical content");
        var file2 = CreateFile("b.bin", "identical content");

        var hash1 = await _hasher.ComputeAsync(file1);
        var hash2 = await _hasher.ComputeAsync(file2);

        Assert.Equal(hash1.Hex, hash2.Hex);
    }

    [Fact]
    public async Task ComputeAsync_DifferentContent_DifferentHash()
    {
        var file1 = CreateFile("a.bin", "content A");
        var file2 = CreateFile("b.bin", "content B");

        var hash1 = await _hasher.ComputeAsync(file1);
        var hash2 = await _hasher.ComputeAsync(file2);

        Assert.NotEqual(hash1.Hex, hash2.Hex);
    }

    [Fact]
    public async Task ComputeAsync_ReportsCorrectFileSize()
    {
        var content = new string('X', 1024);
        var file = CreateFile("sized.bin", content);

        var result = await _hasher.ComputeAsync(file);

        Assert.Equal(new FileInfo(file).Length, result.FileSize);
    }

    [Fact]
    public async Task ComputeAsync_ReportsFilePath()
    {
        var file = CreateFile("path.bin", "data");
        var result = await _hasher.ComputeAsync(file);

        Assert.Equal(file, result.FilePath);
    }

    [Fact]
    public async Task ComputeAsync_ReportsElapsedTime()
    {
        var file = CreateFile("time.bin", "data");
        var result = await _hasher.ComputeAsync(file);

        Assert.True(result.Elapsed >= TimeSpan.Zero);
    }

    [Fact]
    public async Task ComputeAsync_EmptyFile_ProducesKnownHash()
    {
        var file = CreateFile("empty.bin", "");
        var result = await _hasher.ComputeAsync(file);

        // SHA-256 of empty input is well-known
        Assert.Equal("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", result.Hex);
    }

    [Fact]
    public async Task ComputeAsync_NullPath_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _hasher.ComputeAsync(null!));
    }

    [Fact]
    public async Task ComputeAsync_NonexistentFile_Throws()
    {
        await Assert.ThrowsAsync<FileNotFoundException>(
            () => _hasher.ComputeAsync(Path.Combine(_tempDir, "nonexistent.bin")));
    }

    [Fact]
    public async Task ComputeAsync_Cancellation_Throws()
    {
        // Create a larger file to give cancellation a chance
        var file = CreateFile("large.bin", new string('X', 100_000));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _hasher.ComputeAsync(file, cts.Token));
    }

    private string CreateFile(string name, string content)
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, content);
        return path;
    }
}

// ════════════════════════════════════════════════════════════════════════
//  HashResult model
// ════════════════════════════════════════════════════════════════════════

public class HashResultTests
{
    [Fact]
    public void HashResult_RequiredProperties()
    {
        var result = new HashResult
        {
            FilePath = "/test/file.bin",
            Hex = "abc123",
            FileSize = 1024,
            Elapsed = TimeSpan.FromMilliseconds(42),
        };

        Assert.Equal("/test/file.bin", result.FilePath);
        Assert.Equal("abc123", result.Hex);
        Assert.Equal(1024, result.FileSize);
        Assert.Equal(42, result.Elapsed.TotalMilliseconds);
    }
}

// ════════════════════════════════════════════════════════════════════════
//  IngestionCandidate model
// ════════════════════════════════════════════════════════════════════════

public class IngestionCandidateTests
{
    [Fact]
    public void IngestionCandidate_DefaultIsFailed_False()
    {
        var candidate = new IngestionCandidate
        {
            Path = "/test/file.epub",
            EventType = FileEventType.Created,
            DetectedAt = DateTimeOffset.UtcNow,
            ReadyAt = DateTimeOffset.UtcNow,
        };

        Assert.False(candidate.IsFailed);
        Assert.Null(candidate.FailureReason);
        Assert.Null(candidate.OldPath);
        Assert.Null(candidate.Metadata);
    }

    [Fact]
    public void IngestionCandidate_FailedFlag_CanBeSet()
    {
        var candidate = new IngestionCandidate
        {
            Path = "/test/locked.epub",
            EventType = FileEventType.Created,
            DetectedAt = DateTimeOffset.UtcNow,
            ReadyAt = DateTimeOffset.UtcNow,
            IsFailed = true,
            FailureReason = "File probe exhausted",
        };

        Assert.True(candidate.IsFailed);
        Assert.Equal("File probe exhausted", candidate.FailureReason);
    }

    [Fact]
    public void IngestionCandidate_RenameEvent_HasOldPath()
    {
        var candidate = new IngestionCandidate
        {
            Path = "/test/new_name.epub",
            OldPath = "/test/old_name.epub",
            EventType = FileEventType.Renamed,
            DetectedAt = DateTimeOffset.UtcNow,
            ReadyAt = DateTimeOffset.UtcNow,
        };

        Assert.Equal("/test/old_name.epub", candidate.OldPath);
        Assert.Equal(FileEventType.Renamed, candidate.EventType);
    }

    [Fact]
    public void IngestionCandidate_MetadataAndMediaType_NullByDefault()
    {
        var candidate = new IngestionCandidate
        {
            Path = "/test/file.epub",
            EventType = FileEventType.Created,
            DetectedAt = DateTimeOffset.UtcNow,
            ReadyAt = DateTimeOffset.UtcNow,
        };

        Assert.Null(candidate.Metadata);
        Assert.Null(candidate.DetectedMediaType);
    }
}

// ════════════════════════════════════════════════════════════════════════
//  FileEvent model
// ════════════════════════════════════════════════════════════════════════

public class FileEventTests
{
    [Fact]
    public void FileEvent_Created_HasRequiredFields()
    {
        var now = DateTimeOffset.UtcNow;
        var evt = new FileEvent
        {
            Path = "/watch/new_book.epub",
            EventType = FileEventType.Created,
            OccurredAt = now,
        };

        Assert.Equal("/watch/new_book.epub", evt.Path);
        Assert.Equal(FileEventType.Created, evt.EventType);
        Assert.Equal(now, evt.OccurredAt);
        Assert.Null(evt.OldPath);
    }

    [Theory]
    [InlineData(FileEventType.Created)]
    [InlineData(FileEventType.Modified)]
    [InlineData(FileEventType.Deleted)]
    [InlineData(FileEventType.Renamed)]
    public void FileEventType_AllValues_AreDefined(FileEventType type)
    {
        Assert.True(Enum.IsDefined(type));
    }
}
