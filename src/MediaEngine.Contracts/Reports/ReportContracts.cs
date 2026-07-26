using System.Text.Json.Serialization;

namespace MediaEngine.Contracts.Reports;

/// <summary>Request body for submitting a problem report on a media item.</summary>
public sealed class SubmitReportRequest
{
    [JsonPropertyName("entity_id")]
    public Guid EntityId { get; set; }

    [JsonPropertyName("item_title")]
    public string? ItemTitle { get; set; }

    [JsonPropertyName("category")]
    public string? Category { get; set; }

    [JsonPropertyName("note")]
    public string? Note { get; set; }

    [JsonPropertyName("reporter_name")]
    public string? ReporterName { get; set; }
}

/// <summary>Response returned after a report mutation.</summary>
public sealed class SubmitReportResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";
}

/// <summary>A previously submitted problem report.</summary>
public sealed class ReportEntryResponse
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("occurred_at")]
    public string OccurredAt { get; set; } = "";

    [JsonPropertyName("category")]
    public string Category { get; set; } = "";

    [JsonPropertyName("note")]
    public string Note { get; set; } = "";

    [JsonPropertyName("reporter_name")]
    public string ReporterName { get; set; } = "";

    [JsonPropertyName("detail")]
    public string? Detail { get; set; }
}
