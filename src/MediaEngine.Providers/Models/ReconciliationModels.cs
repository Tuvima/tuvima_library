namespace MediaEngine.Providers.Models;

/// <summary>Audiobook edition metadata discovered via P747 + P31 filtering.</summary>
public sealed record AudiobookEditionData(string? EditionQid, string? WorkLabel, string? Narrator, string? Duration, string? ASIN, string? Publisher);

/// <summary>A P279 class-to-media-type mapping learned at runtime.</summary>
public sealed record LearnedClassEntry(string ClassQID, string MediaType, string ParentQID, DateTime LearnedAt);
