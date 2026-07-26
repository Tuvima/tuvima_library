namespace MediaEngine.Domain.Models;

public sealed record ItemCanonicalWorkAssetContext(
    Guid AssetId,
    string MediaType,
    string? WorkTitle,
    string? PrimaryCreator,
    string? Year);

public sealed record ItemCanonicalWorkWikidataState(
    string? Qid,
    string? Status,
    string? Source,
    bool Locked,
    string? RejectedQidsJson);

public sealed record ItemCanonicalDisplayOverrideState(
    bool WorkExists,
    Dictionary<string, string> Values);

public sealed record ItemCanonicalIdentityArtifact(Guid EntityId, string Key);
