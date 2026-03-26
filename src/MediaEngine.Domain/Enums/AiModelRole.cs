namespace MediaEngine.Domain.Enums;

/// <summary>
/// The functional role a model serves in the AI pipeline.
/// Each role maps to a model file in config/ai.json.
/// Models are assigned to roles at first-run setup based on hardware.
/// </summary>
public enum AiModelRole
{
    /// <summary>Fast text model for on-demand tasks (search parsing, TL;DR, Why Factor).</summary>
    TextFast = 1,

    /// <summary>Quality text model for batch/scheduled tasks (ingestion manifest, vibe tags, disambiguation).</summary>
    TextQuality = 2,

    /// <summary>Audio model for Whisper tasks (transcription, language detection, sync maps).</summary>
    Audio = 3,

    /// <summary>Scholar text model for deep enrichment (description intelligence, complex analysis). Scheduled/overnight.</summary>
    TextScholar = 4,
}
