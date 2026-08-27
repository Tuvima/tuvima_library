using System.Text.Json.Serialization;

namespace MediaEngine.Contracts.Ingestion;

/// <summary>
/// Wire shape for <c>POST /ingestion/reconcile</c>.
///
/// Property names are deliberately lower_snake_case with no
/// <see cref="System.Text.Json.Serialization.JsonPropertyNameAttribute"/> — they reproduce, byte
/// for byte, the anonymous object this record replaces.
/// </summary>
public sealed class ReconciliationResultResponse
{
    [JsonPropertyName("total_scanned")]
    public int TotalScanned { get; set; }

    [JsonPropertyName("missing_count")]
    public int MissingCount { get; set; }

    [JsonPropertyName("elapsed_ms")]
    public long ElapsedMs { get; set; }

    [JsonPropertyName("duplicate_read_works_merged")]
    public int DuplicateReadWorksMerged { get; set; }

    [JsonPropertyName("audiobook_authors_aligned")]
    public int AudiobookAuthorsAligned { get; set; }
}

/// <summary>
/// Wire shape for <c>GET /ingestion/batches/attention-count</c>.
/// </summary>
public sealed record BatchAttentionCountResponse(int count);

/// <summary>
/// Wire shape for <c>POST /ingestion/upload</c>.
/// </summary>
public sealed record UploadMediaResponse(string path, string mediaType, string destinationLibraryId);

/// <summary>Wire shape for <c>POST /ingestion/rescan</c>.</summary>
public sealed record RescanAcceptedResponse(string message, int paths_scanned);
