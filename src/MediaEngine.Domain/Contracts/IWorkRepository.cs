namespace MediaEngine.Domain.Contracts;

/// <summary>Repository for querying Work entities by identity attributes.</summary>
public interface IWorkRepository
{
    /// <summary>
    /// Finds an existing work matching the given title, author, and media type.
    /// Uses case-insensitive matching on title and author.
    /// Author match is optional — when <paramref name="author"/> is null or empty,
    /// only title and media type are compared.
    /// Returns null if no match is found.
    /// </summary>
    Task<WorkMatch?> FindByTitleAuthorAsync(
        string title,
        string? author,
        string mediaType,
        CancellationToken ct = default);
}

/// <summary>Result of a work lookup query.</summary>
public sealed record WorkMatch(Guid WorkId, string? WikidataQid);
