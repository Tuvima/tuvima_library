using System.Text.Json.Serialization;

namespace MediaEngine.Contracts.Realtime;

public sealed record FolderHealthChangedEvent(
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("is_accessible")] bool IsAccessible,
    [property: JsonPropertyName("has_read")] bool HasRead,
    [property: JsonPropertyName("has_write")] bool HasWrite,
    [property: JsonPropertyName("checked_at")] DateTimeOffset CheckedAt);

public sealed record RetagSweepProgressEvent(
    int Processed,
    int Succeeded,
    int Transient,
    int Terminal,
    bool IsFinal);

public sealed record RetagSweepCompletedEvent(
    int Processed,
    int Succeeded,
    int Transient,
    int Terminal);

public sealed record InitialSweepStartedEvent(
    [property: JsonPropertyName("roots")] IReadOnlyList<string> Roots,
    [property: JsonPropertyName("started_at")] DateTimeOffset StartedAt);

public sealed record InitialSweepProgressEvent(
    [property: JsonPropertyName("discovered")] int Discovered,
    [property: JsonPropertyName("processed")] int Processed,
    [property: JsonPropertyName("hashed")] int Hashed,
    [property: JsonPropertyName("cached")] int Cached,
    [property: JsonPropertyName("failed")] int Failed,
    [property: JsonPropertyName("bytes_hashed")] long BytesHashed);

public sealed record InitialSweepCompletedEvent(
    [property: JsonPropertyName("discovered")] int Discovered,
    [property: JsonPropertyName("hashed")] int Hashed,
    [property: JsonPropertyName("cached")] int Cached,
    [property: JsonPropertyName("failed")] int Failed,
    [property: JsonPropertyName("bytes_hashed")] long BytesHashed,
    [property: JsonPropertyName("elapsed_seconds")] double ElapsedSeconds,
    [property: JsonPropertyName("completed_at")] DateTimeOffset CompletedAt);
