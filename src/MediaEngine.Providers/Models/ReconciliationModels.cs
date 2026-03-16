namespace MediaEngine.Providers.Models;

/// <summary>Single candidate from the OpenRefine Reconciliation API.</summary>
public sealed record ReconciliationCandidate(string QID, string Label, string? Description, double Score, bool Match);

/// <summary>Data extension result for one entity.</summary>
public sealed record ExtensionResult(string QID, Dictionary<string, List<ExtensionValue>> Properties);

/// <summary>A single property value from the Data Extension API. Exactly one field is non-null.</summary>
public sealed record ExtensionValue(string? Str, string? Id, string? Label, string? Date, string? Float);

/// <summary>Audiobook edition metadata discovered via P747 + P31 filtering.</summary>
public sealed record AudiobookEditionData(string? Narrator, string? Duration, string? ASIN, string? Publisher);

/// <summary>A P279 class-to-media-type mapping learned at runtime.</summary>
public sealed record LearnedClassEntry(string ClassQID, string MediaType, string ParentQID, DateTime LearnedAt);

/// <summary>A single reconciliation request for batch operations.</summary>
public sealed record ReconcileRequest(string QueryId, string Query, Dictionary<string, string>? PropertyConstraints = null);
