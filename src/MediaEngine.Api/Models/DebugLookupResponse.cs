namespace MediaEngine.Api.Models;

/// <summary>
/// Full enrichment result returned by <c>POST /debug/lookup</c>.
/// All data is ephemeral — nothing is persisted to the database.
/// </summary>
public sealed record DebugLookupResponse(
    string? ResolvedQid,
    List<DebugClaimGroup> ClaimGroups,
    List<DebugPersonResult> Persons,
    List<DebugEntityResult> FictionalEntities,
    List<DebugRelationshipResult> Relationships,
    List<DebugBridgeHint> BridgeHintPreview);

/// <summary>All claims for a single metadata field key.</summary>
public sealed record DebugClaimGroup(string FieldKey, List<DebugClaimEntry> Claims);

/// <summary>A single claim value returned by the provider.</summary>
public sealed record DebugClaimEntry(string Value, double Confidence, string ProviderId);

/// <summary>A person found in the database whose QID appears in the enrichment claims.</summary>
public sealed record DebugPersonResult(
    string Name,
    string Role,
    string? Qid,
    string? HeadshotUrl,
    string? Biography,
    string? Occupation);

/// <summary>A fictional entity found in the database whose QID appears in the enrichment claims.</summary>
public sealed record DebugEntityResult(
    string Label,
    string? Qid,
    string EntityType,
    string? Description,
    string? ImageUrl);

/// <summary>A relationship edge between two entities in the universe graph.</summary>
public sealed record DebugRelationshipResult(
    string SubjectQid,
    string SubjectLabel,
    string RelationshipType,
    string ObjectQid,
    string ObjectLabel,
    string? StartTime,
    string? EndTime);

/// <summary>A preview of what bridge hints would flow to Stage 2 retail providers.</summary>
public sealed record DebugBridgeHint(
    string Key,
    string RawValue,
    string NormalizedValue,
    string SourceClaimKey,
    List<string> TargetProviders);

/// <summary>A single Wikidata reconciliation candidate.</summary>
public sealed record DebugSearchCandidate(
    string Qid,
    string Label,
    string? Description,
    double Score,
    bool Match,
    string WikidataUrl);

/// <summary>Response from <c>POST /debug/search</c>.</summary>
public sealed record DebugSearchResponse(List<DebugSearchCandidate> Candidates);
