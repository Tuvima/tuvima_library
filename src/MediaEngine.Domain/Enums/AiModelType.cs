namespace MediaEngine.Domain.Enums;

/// <summary>
/// Identifies the type of AI model (text vs audio inference).
/// </summary>
public enum AiModelType
{
    /// <summary>Text inference via LLamaSharp (Llama, Mistral, Phi, etc.).</summary>
    Text = 1,

    /// <summary>Audio inference via Whisper.net (speech-to-text, language detection).</summary>
    Audio = 2,
}
