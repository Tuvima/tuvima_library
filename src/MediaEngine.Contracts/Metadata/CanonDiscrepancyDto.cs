namespace MediaEngine.Contracts.Metadata;

/// <summary>A field where an edition differs from its master work.</summary>
public sealed record CanonDiscrepancyDto(
    string FieldKey,
    string MasterWorkValue,
    string EditionValue,
    string MasterWorkQid);
