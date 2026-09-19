namespace MediaEngine.Providers.Models;

/// <summary>
/// Statement-level Wikidata evidence for one fictional entity. This travels beside
/// flattened metadata claims so relationship qualifiers never enter canonical-value
/// scoring or lose their source-statement association.
/// </summary>
public sealed record FictionalEntityGraphEvidence(
    string SourceProvider,
    string Provenance,
    string? NarrativeUniverseQid,
    string? NarrativeUniverseLabel,
    IReadOnlyList<FictionalEntityRelationshipStatement> Statements,
    IReadOnlyList<FictionalEntityScalarStatement> ScalarStatements);

/// <summary>A single entity-valued Wikidata statement with its original qualifiers.</summary>
public sealed record FictionalEntityRelationshipStatement(
    string ClaimKey,
    string TargetQid,
    string? TargetLabel,
    double Confidence,
    IReadOnlyList<FictionalEntityStatementQualifier> Qualifiers,
    string SourceProvider = "wikidata",
    string Provenance = "Wikidata");

/// <summary>
/// A scalar Wikidata statement retained beside graph edges. Event chronology is
/// scalar rather than entity-valued, so it must not be discarded by edge discovery.
/// </summary>
public sealed record FictionalEntityScalarStatement(
    string ClaimKey,
    string Value,
    string ValueKind,
    double Confidence,
    IReadOnlyList<FictionalEntityStatementQualifier> Qualifiers,
    string SourceProvider = "wikidata",
    string Provenance = "Wikidata");

/// <summary>Typed value retained from a Wikidata statement qualifier.</summary>
public sealed record FictionalEntityStatementQualifier(
    string PropertyId,
    string Value,
    string ValueKind);
