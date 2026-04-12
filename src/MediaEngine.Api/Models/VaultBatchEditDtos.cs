using System.Text.Json.Serialization;

namespace MediaEngine.Api.Models;

/// <summary>Request to apply batch field edits to multiple items.</summary>
public sealed class VaultBatchEditRequest
{
    [JsonPropertyName("entity_ids")]
    public List<Guid> EntityIds { get; init; } = [];

    [JsonPropertyName("field_changes")]
    public List<VaultFieldChange> FieldChanges { get; init; } = [];
}

/// <summary>A single field change to apply.</summary>
public sealed class VaultFieldChange
{
    [JsonPropertyName("key")]
    public required string Key { get; init; }

    [JsonPropertyName("value")]
    public required string Value { get; init; }
}

/// <summary>Result of a batch edit operation.</summary>
public sealed class VaultBatchEditResult
{
    [JsonPropertyName("updated_count")]
    public int UpdatedCount { get; init; }

    [JsonPropertyName("failed_ids")]
    public List<Guid> FailedIds { get; init; } = [];

    [JsonPropertyName("errors")]
    public List<string> Errors { get; init; } = [];
}

/// <summary>Dry-run preview of a batch edit operation.</summary>
public sealed class VaultBatchEditPreview
{
    [JsonPropertyName("affected_count")]
    public int AffectedCount { get; init; }

    [JsonPropertyName("changes")]
    public List<VaultFieldChangePreview> Changes { get; init; } = [];
}

/// <summary>Preview of how a single field change would affect items.</summary>
public sealed class VaultFieldChangePreview
{
    [JsonPropertyName("key")]
    public required string Key { get; init; }

    [JsonPropertyName("new_value")]
    public required string NewValue { get; init; }

    /// <summary>Count of items that currently have each distinct old value.</summary>
    [JsonPropertyName("old_value_counts")]
    public Dictionary<string, int> OldValueCounts { get; init; } = new();
}
