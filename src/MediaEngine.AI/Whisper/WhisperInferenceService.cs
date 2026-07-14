using MediaEngine.AI.Configuration;
using MediaEngine.AI.Infrastructure;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace MediaEngine.AI.Whisper;

/// <summary>Runs Whisper inference under the shared model-runtime lease.</summary>
public sealed class WhisperInferenceService : IAudioTranscriptionService, IAsyncDisposable
{
    private readonly AiModelRuntimeDefinition _audioDefinition;
    private readonly IModelRuntimeCoordinator _runtimeCoordinator;
    private readonly ModelInventory _inventory;
    private readonly IWhisperExecutionBackend _backend;
    private readonly ILogger<WhisperInferenceService> _logger;
    private readonly IDisposable _disposerRegistration;
    private int _disposed;

    public WhisperInferenceService(
        AiSettings settings,
        IModelLifecycleManager lifecycle,
        ModelInventory inventory,
        ILogger<WhisperInferenceService> logger)
        : this(settings, lifecycle, inventory, logger, new WhisperExecutionBackend())
    {
    }

    internal WhisperInferenceService(
        AiSettings settings,
        IModelLifecycleManager lifecycle,
        ModelInventory inventory,
        ILogger<WhisperInferenceService> logger,
        IWhisperExecutionBackend backend)
    {
        _audioDefinition = AiRuntimeSettingsSnapshot.Create(settings).GetModel(AiModelRole.Audio);
        _runtimeCoordinator = lifecycle as IModelRuntimeCoordinator
            ?? throw new ArgumentException(
                $"{nameof(IModelLifecycleManager)} must also implement {nameof(IModelRuntimeCoordinator)}.",
                nameof(lifecycle));
        _inventory = inventory;
        _backend = backend;
        _logger = logger;
        _disposerRegistration = _runtimeCoordinator.RegisterModelDisposer(_backend.DisposeModelAsync);
    }

    public async Task<IReadOnlyList<TranscriptionSegment>> TranscribeAsync(
        string wavFilePath,
        CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (!File.Exists(wavFilePath))
        {
            _logger.LogWarning("WAV file not found for transcription: {Path}", wavFilePath);
            return [];
        }

        var language = string.IsNullOrWhiteSpace(_audioDefinition.Language)
            ? "auto"
            : _audioDefinition.Language;
        await using var lease = await _runtimeCoordinator
            .AcquireInferenceLeaseAsync(AiModelRole.Audio, ct)
            .ConfigureAwait(false);
        var segments = await _backend.TranscribeAsync(
            _inventory.GetModelPath(AiModelRole.Audio),
            language,
            _audioDefinition.Translate,
            wavFilePath,
            ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Transcription complete: {Count} segments from {Path}",
            segments.Count,
            wavFilePath);
        return segments;
    }

    public async Task<(string LanguageCode, double Confidence)> DetectLanguageAsync(
        string wavFilePath,
        CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (!File.Exists(wavFilePath))
        {
            _logger.LogWarning("WAV file not found for language detection: {Path}", wavFilePath);
            return ("en", 0.0);
        }

        try
        {
            await using var lease = await _runtimeCoordinator
                .AcquireInferenceLeaseAsync(AiModelRole.Audio, ct)
                .ConfigureAwait(false);
            var result = await _backend.DetectLanguageAsync(
                _inventory.GetModelPath(AiModelRole.Audio),
                wavFilePath,
                ct).ConfigureAwait(false);
            _logger.LogInformation(
                "Detected language: {Language} (confidence={Confidence:F2}) for {Path}",
                result.LanguageCode,
                result.Confidence,
                wavFilePath);
            return result;
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Whisper model is unavailable for language detection");
            return ("en", 0.0);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _disposerRegistration.Dispose();
        await _backend.DisposeAsync().ConfigureAwait(false);
    }
}

public sealed class TranscriptionSegment
{
    public long StartMs { get; init; }
    public long EndMs { get; init; }
    public required string Text { get; init; }
    public double Confidence { get; init; }
}
