namespace MediaEngine.Domain.Contracts;

/// <summary>
/// AI-powered cover art validation. Compares downloaded cover art metadata
/// against work metadata to detect mismatches.
/// </summary>
public interface ICoverArtValidator
{
    /// <summary>
    /// Validate that a cover image matches the expected work.
    /// Returns a validation result with confidence and issue description.
    /// </summary>
    Task<CoverValidationResult> ValidateAsync(
        string workTitle,
        string? workAuthor,
        int? workYear,
        string coverFilePath,
        CancellationToken ct = default);
}

/// <summary>
/// Result of cover art validation.
/// </summary>
public sealed class CoverValidationResult
{
    /// <summary>Whether the cover art appears to match the work.</summary>
    public bool IsValid { get; init; }

    /// <summary>Confidence in the validation (0.0-1.0).</summary>
    public double Confidence { get; init; }

    /// <summary>Description of the issue if not valid.</summary>
    public string? Issue { get; init; }
}
