namespace MediaEngine.Contracts.Ingestion;

/// <summary>
/// Wire shape for <c>POST /ingestion/reconcile</c>.
///
/// Property names are deliberately lower_snake_case with no
/// <see cref="System.Text.Json.Serialization.JsonPropertyNameAttribute"/> — they reproduce, byte
/// for byte, the anonymous object this record replaces.
/// </summary>
public sealed record ReconciliationResultResponse(int total_scanned, int missing_count, long elapsed_ms);

/// <summary>
/// Wire shape for <c>GET /ingestion/batches/attention-count</c>.
/// </summary>
public sealed record BatchAttentionCountResponse(int count);

/// <summary>
/// Wire shape for <c>POST /ingestion/upload</c>.
/// </summary>
public sealed record UploadMediaResponse(string path, string mediaType);

/// <summary>Wire shape for <c>POST /ingestion/rescan</c>.</summary>
public sealed record RescanAcceptedResponse(string message, int paths_scanned);
