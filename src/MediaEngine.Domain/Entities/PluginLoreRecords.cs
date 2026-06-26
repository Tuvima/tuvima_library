namespace MediaEngine.Domain.Entities;

public static class PluginLoreSourceStatus
{
    public const string Pending = "Pending";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
}

public sealed class PluginLoreSourceRecord
{
    public Guid Id { get; set; }
    public string UniverseQid { get; set; } = "";
    public string PluginId { get; set; } = "";
    public string SourceKey { get; set; } = "";
    public string SourceName { get; set; } = "";
    public string BaseUrl { get; set; } = "";
    public string ApiUrl { get; set; } = "";
    public string Status { get; set; } = PluginLoreSourceStatus.Pending;
    public double Confidence { get; set; }
    public string EvidenceJson { get; set; } = "{}";
    public string? License { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTimeOffset? RejectedAt { get; set; }
    public DateTimeOffset? LastDiscoveredAt { get; set; }
    public DateTimeOffset? LastEnrichedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class PluginLoreSourceCandidateRecord
{
    public string SourceKey { get; set; } = "";
    public string SourceName { get; set; } = "";
    public string BaseUrl { get; set; } = "";
    public string ApiUrl { get; set; } = "";
    public string? License { get; set; }
    public double Confidence { get; set; }
    public string EvidenceJson { get; set; } = "{}";
}

public sealed class PluginLoreEntityRecord
{
    public Guid Id { get; set; }
    public Guid SourceId { get; set; }
    public string UniverseQid { get; set; } = "";
    public string PluginId { get; set; } = "";
    public string ExternalKey { get; set; } = "";
    public string? WikidataQid { get; set; }
    public string Label { get; set; } = "";
    public string? Description { get; set; }
    public string EntityType { get; set; } = "Unknown";
    public string AliasesJson { get; set; } = "[]";
    public string SourceUrl { get; set; } = "";
    public double Confidence { get; set; }
    public string EvidenceJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class PluginLoreRelationshipRecord
{
    public Guid Id { get; set; }
    public Guid SourceId { get; set; }
    public string UniverseQid { get; set; } = "";
    public string PluginId { get; set; } = "";
    public string SubjectExternalKey { get; set; } = "";
    public string? SubjectQid { get; set; }
    public string ObjectExternalKey { get; set; } = "";
    public string? ObjectQid { get; set; }
    public string RelationshipType { get; set; } = "";
    public string SourceUrl { get; set; } = "";
    public double Confidence { get; set; }
    public string EvidenceJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
