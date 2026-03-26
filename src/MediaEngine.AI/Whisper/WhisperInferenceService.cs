using MediaEngine.AI.Configuration;
using MediaEngine.AI.Infrastructure;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace MediaEngine.AI.Whisper;

/// <summary>
/// Core Whisper inference service wrapping Whisper.net.
/// Handles model loading, audio transcription, and language detection.
/// </summary>
public sealed class WhisperInferenceService
{
    private readonly AiSettings _settings;
    private readonly IModelLifecycleManager _lifecycle;
    private readonly ModelInventory _inventory;
    private readonly ILogger<WhisperInferenceService> _logger;

    public WhisperInferenceService(
        AiSettings settings,
        IModelLifecycleManager lifecycle,
        ModelInventory inventory,
        ILogger<WhisperInferenceService> logger)
    {
        _settings = settings;
        _lifecycle = lifecycle;
        _inventory = inventory;
        _logger = logger;
    }

    /// <summary>
    /// Transcribe an audio file and return timestamped segments.
    /// Requires a 16kHz mono WAV file (use AudioPreprocessor to convert).
    /// </summary>
    public async Task<IReadOnlyList<TranscriptionSegment>> TranscribeAsync(
        string wavFilePath,
        CancellationToken ct = default)
    {
        var loaded = await _lifecycle.EnsureLoadedAsync(AiModelRole.Audio, ct);
        if (!loaded)
            throw new InvalidOperationException("Failed to load Whisper model");

        var modelPath = _inventory.GetModelPath(AiModelRole.Audio);
        _logger.LogInformation("Transcribing: {Path}", wavFilePath);

        // Whisper.net integration will load the model and process audio.
        // Full implementation requires Whisper.net WhisperFactory + WhisperProcessor.
        // Placeholder returns empty — Sprint 5 will wire the actual Whisper.net calls.
        _logger.LogWarning("Whisper transcription not yet wired — awaiting Sprint 5 full implementation");
        return [];
    }

    /// <summary>
    /// Detect the spoken language of an audio file (first 30 seconds).
    /// Returns ISO 639-1 language code (e.g. "en", "es", "fr").
    /// </summary>
    public async Task<(string LanguageCode, double Confidence)> DetectLanguageAsync(
        string wavFilePath,
        CancellationToken ct = default)
    {
        var loaded = await _lifecycle.EnsureLoadedAsync(AiModelRole.Audio, ct);
        if (!loaded)
            return ("en", 0.0);

        _logger.LogInformation("Detecting language: {Path}", wavFilePath);

        // Whisper.net language detection — placeholder.
        _logger.LogWarning("Whisper language detection not yet wired — defaulting to 'en'");
        return ("en", 0.5);
    }
}

/// <summary>
/// A single transcription segment with timestamps.
/// </summary>
public sealed class TranscriptionSegment
{
    /// <summary>Start time in milliseconds from file beginning.</summary>
    public long StartMs { get; init; }

    /// <summary>End time in milliseconds from file beginning.</summary>
    public long EndMs { get; init; }

    /// <summary>Transcribed text for this segment.</summary>
    public required string Text { get; init; }

    /// <summary>Confidence of the transcription (0.0-1.0).</summary>
    public double Confidence { get; init; }
}
