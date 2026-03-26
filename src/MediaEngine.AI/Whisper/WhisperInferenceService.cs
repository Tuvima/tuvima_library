using MediaEngine.AI.Configuration;
using MediaEngine.AI.Infrastructure;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Enums;
using Microsoft.Extensions.Logging;
using Whisper.net;

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

        if (!File.Exists(wavFilePath))
        {
            _logger.LogWarning("WAV file not found for transcription: {Path}", wavFilePath);
            return [];
        }

        var modelPath = _inventory.GetModelPath(AiModelRole.Audio);
        var audioDefinition = _settings.Models.Audio;
        var language = string.IsNullOrWhiteSpace(audioDefinition.Language) ? "auto" : audioDefinition.Language;

        _logger.LogInformation("Transcribing: {Path} (language={Language})", wavFilePath, language);

        var segments = new List<TranscriptionSegment>();

        using var factory = WhisperFactory.FromPath(modelPath);
        var builder = factory.CreateBuilder()
            .WithLanguage(language);

        if (audioDefinition.Translate)
            builder = builder.WithTranslate();

        using var processor = builder.Build();

        await using var wavStream = File.OpenRead(wavFilePath);
        await foreach (var segment in processor.ProcessAsync(wavStream, ct))
        {
            segments.Add(new TranscriptionSegment
            {
                StartMs = (long)segment.Start.TotalMilliseconds,
                EndMs = (long)segment.End.TotalMilliseconds,
                Text = segment.Text.Trim(),
                Confidence = segment.Probability,
            });
        }

        _logger.LogInformation("Transcription complete: {Count} segments from {Path}", segments.Count, wavFilePath);
        return segments;
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

        if (!File.Exists(wavFilePath))
        {
            _logger.LogWarning("WAV file not found for language detection: {Path}", wavFilePath);
            return ("en", 0.0);
        }

        var modelPath = _inventory.GetModelPath(AiModelRole.Audio);
        _logger.LogInformation("Detecting language: {Path}", wavFilePath);

        using var factory = WhisperFactory.FromPath(modelPath);
        using var processor = factory.CreateBuilder()
            .WithLanguageDetection()
            .Build();

        await using var wavStream = File.OpenRead(wavFilePath);

        // Process segments; the first segment carries the detected language code.
        string detectedLanguage = "en";
        double confidence = 0.0;

        await foreach (var segment in processor.ProcessAsync(wavStream, ct))
        {
            if (!string.IsNullOrWhiteSpace(segment.Language))
            {
                detectedLanguage = segment.Language;
                confidence = segment.Probability;
            }
            // Only need the first segment for language detection.
            break;
        }

        _logger.LogInformation("Detected language: {Language} (confidence={Confidence:F2}) for {Path}",
            detectedLanguage, confidence, wavFilePath);

        return (detectedLanguage, confidence);
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
