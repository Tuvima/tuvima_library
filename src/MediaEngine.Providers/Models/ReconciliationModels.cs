namespace MediaEngine.Providers.Models;

/// <summary>Audiobook edition metadata discovered via P747 + P31 filtering.</summary>
public sealed record AudiobookEditionData(string? EditionQid, string? WorkLabel, string? Narrator, string? Duration, string? ASIN, string? Publisher);