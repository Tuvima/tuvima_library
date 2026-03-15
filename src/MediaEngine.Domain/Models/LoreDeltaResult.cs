namespace MediaEngine.Domain.Models;

/// <summary>Result of comparing a cached Wikidata revision ID against the current one.</summary>
public sealed record LoreDeltaResult(
    string EntityQid,
    string Label,
    long CachedRevision,
    long CurrentRevision,
    bool HasChanged);
