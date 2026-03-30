using MediaEngine.AI.Llama;
using MediaEngine.Domain;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace MediaEngine.AI.Features;

public sealed class CoverArtValidator : ICoverArtValidator
{
    private readonly ILlamaInferenceService _llama;
    private readonly ILogger<CoverArtValidator> _logger;

    public CoverArtValidator(ILlamaInferenceService llama, ILogger<CoverArtValidator> logger)
    {
        _llama = llama;
        _logger = logger;
    }

    public async Task<CoverValidationResult> ValidateAsync(
        string workTitle,
        string? workAuthor,
        int? workYear,
        string coverFilePath,
        CancellationToken ct = default)
    {
        // Phase 1: text-based validation using file metadata.
        // Full vision-based validation deferred to future LLM upgrade.
        if (!File.Exists(coverFilePath))
            return new CoverValidationResult { IsValid = false, Confidence = 1.0, Issue = "Cover file not found" };

        var fileInfo = new FileInfo(coverFilePath);

        // Basic checks that don't need LLM.
        if (fileInfo.Length < 1024) // < 1KB
            return new CoverValidationResult { IsValid = false, Confidence = ClaimConfidence.CoverArtInvalid, Issue = "Cover file is too small (< 1KB) — likely a placeholder" };

        if (fileInfo.Length > 50 * 1024 * 1024) // > 50MB
            return new CoverValidationResult { IsValid = false, Confidence = ClaimConfidence.CoverArtInvalid, Issue = "Cover file is unusually large (> 50MB)" };

        // For now, basic validation passes. LLM-based comparison will be added
        // when vision models are available (Llama 3.2 Vision or similar).
        _logger.LogDebug("CoverArtValidator: basic validation passed for \"{Title}\"", workTitle);

        return new CoverValidationResult
        {
            IsValid = true,
            Confidence = ClaimConfidence.CoverArtValid,
            Issue = null,
        };
    }
}
