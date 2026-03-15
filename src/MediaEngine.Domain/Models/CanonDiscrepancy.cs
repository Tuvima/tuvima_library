namespace MediaEngine.Domain.Models;

/// <summary>A field where the edition's value differs from the master work's value.</summary>
public sealed record CanonDiscrepancy(
    string FieldKey,
    string MasterWorkValue,
    string EditionValue,
    string MasterWorkQid);
