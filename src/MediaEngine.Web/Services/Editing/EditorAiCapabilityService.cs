using MediaEngine.Web.Services.Integration;

namespace MediaEngine.Web.Services.Editing;

public static class EditorAiFeatureKeys
{
    public const string AudiobookChapterNaming = "audiobook_chapter_naming";
}

public sealed record EditorAiCapability(bool IsAvailable, string Explanation)
{
    public static EditorAiCapability Available { get; } = new(true, "Local AI is ready.");
}

public sealed class EditorAiCapabilityService(UIOrchestratorService orchestrator)
{
    public async Task<EditorAiCapability> GetAudiobookChapterNamingAsync(CancellationToken ct = default)
    {
        var config = await orchestrator.GetAiConfigAsync(ct);
        if (config is null)
            return new(false, "AI configuration is unavailable.");

        if (!config.Features.TryGetValue(EditorAiFeatureKeys.AudiobookChapterNaming, out var enabled) || !enabled)
            return new(false, "Enable Audiobook chapter naming in Local AI settings.");

        var status = await orchestrator.GetAiStatusAsync(ct);
        if (status is null || !status.IsReady)
            return new(false, "The local AI subsystem is not ready.");

        var models = await orchestrator.GetAiModelStatusesAsync(ct);
        var model = models.FirstOrDefault(candidate =>
            string.Equals(candidate.Role, "text_quality", StringComparison.OrdinalIgnoreCase));
        if (model is null || (!model.Loaded && !model.Active && !string.Equals(model.State, "Ready", StringComparison.OrdinalIgnoreCase)))
            return new(false, "The text quality model is not ready.");

        return EditorAiCapability.Available;
    }
}
