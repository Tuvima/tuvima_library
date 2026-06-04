namespace MediaEngine.AI.Whisper;

public interface IAudioTranscriptionService
{
    Task<IReadOnlyList<TranscriptionSegment>> TranscribeAsync(
        string wavFilePath,
        CancellationToken ct = default);

    Task<(string LanguageCode, double Confidence)> DetectLanguageAsync(
        string wavFilePath,
        CancellationToken ct = default);
}
