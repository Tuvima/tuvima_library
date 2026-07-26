namespace MediaEngine.Contracts.Development;

/// <summary>
/// Request body for a live reconciliation and data-extension lookup that does not persist data.
/// </summary>
public sealed record DebugLookupRequest(
    string Title,
    string MediaType,
    string? Author = null);

/// <summary>Request body for enriching a caller-confirmed Wikidata item.</summary>
public sealed record DebugEnrichRequest(
    string Qid,
    string MediaType,
    string? Author = null);

/// <summary>Complete ephemeral enrichment result returned by the debug endpoints.</summary>
public sealed record DebugLookupResponse(
    string? ResolvedQid,
    List<DebugClaimGroup> ClaimGroups,
    List<DebugPersonResult> Persons,
    List<DebugEntityResult> FictionalEntities,
    List<DebugRelationshipResult> Relationships,
    List<DebugBridgeHint> BridgeHintPreview);

public sealed record DebugClaimGroup(string FieldKey, List<DebugClaimEntry> Claims);

public sealed record DebugClaimEntry(string Value, double Confidence, string ProviderId);

public sealed record DebugPersonResult(
    string Name,
    string Role,
    string? Qid,
    string? HeadshotUrl,
    string? Biography,
    string? Occupation);

public sealed record DebugEntityResult(
    string Label,
    string? Qid,
    string EntityType,
    string? Description,
    string? ImageUrl);

public sealed record DebugRelationshipResult(
    string SubjectQid,
    string SubjectLabel,
    string RelationshipType,
    string ObjectQid,
    string ObjectLabel,
    string? StartTime,
    string? EndTime);

public sealed record DebugBridgeHint(
    string Key,
    string RawValue,
    string NormalizedValue,
    string SourceClaimKey,
    List<string> TargetProviders);

public sealed record DebugSearchCandidate(
    string Qid,
    string Label,
    string? Description,
    double Score,
    bool Match,
    string WikidataUrl);

public sealed record DebugSearchResponse(List<DebugSearchCandidate> Candidates);
