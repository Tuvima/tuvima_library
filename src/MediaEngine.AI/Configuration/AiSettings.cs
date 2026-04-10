using System.Text.Json.Serialization;

namespace MediaEngine.AI.Configuration;

/// <summary>
/// Typed configuration model for <c>config/ai.json</c>.
/// </summary>
public sealed class AiSettings
{
    /// <summary>Dev-only flag to skip model download requirement. Not exposed in Dashboard.</summary>
    [JsonPropertyName("dev_skip_download")]
    public bool DevSkipDownload { get; set; }

    /// <summary>Directory where model files are stored (Docker: /models).</summary>
    [JsonPropertyName("models_directory")]
    public string ModelsDirectory { get; set; } = "/models";

    /// <summary>Seconds of idle time before auto-unloading a model from memory.</summary>
    [JsonPropertyName("idle_unload_seconds")]
    public int IdleUnloadSeconds { get; set; } = 300;

    /// <summary>Maximum seconds to wait for an inference call before timeout.</summary>
    [JsonPropertyName("inference_timeout_seconds")]
    public int InferenceTimeoutSeconds { get; set; } = 60;

    /// <summary>Model definitions by role.</summary>
    [JsonPropertyName("models")]
    public AiModelDefinitions Models { get; set; } = new();

    /// <summary>Per-feature enable/disable flags.</summary>
    [JsonPropertyName("features")]
    public AiFeatureFlags Features { get; set; } = new();

    /// <summary>Per-category controlled vocabulary for vibe tags.</summary>
    [JsonPropertyName("vibe_vocabulary")]
    public AiVibeVocabulary VibeVocabulary { get; set; } = new();

    /// <summary>Cron schedules for background AI tasks.</summary>
    [JsonPropertyName("scheduling")]
    public AiScheduling Scheduling { get; set; } = new();

    /// <summary>Number of entities to process per batch run in background enrichment services.</summary>
    [JsonPropertyName("enrichment_batch_size")]
    public int EnrichmentBatchSize { get; set; } = 10;

    /// <summary>Hardware profiling result — populated once at startup by HardwareBenchmarkService.</summary>
    [JsonPropertyName("hardware_profile")]
    public HardwareProfile HardwareProfile { get; set; } = new();
}

/// <summary>Model definitions by role (text_fast, text_quality, audio).</summary>
public sealed class AiModelDefinitions
{
    [JsonPropertyName("text_fast")]
    public AiModelDefinition TextFast { get; set; } = new()
    {
        Description = "On-demand text tasks (search parsing, TL;DR, Why Factor)",
        File = "llama-3.2-1b-instruct.Q4_K_M.gguf",
        DownloadUrl = "https://huggingface.co/bartowski/Llama-3.2-1B-Instruct-GGUF/resolve/main/Llama-3.2-1B-Instruct-Q4_K_M.gguf",
        SizeMB = 750,
        ContextLength = 2048,
        MaxTokens = 256,
        Temperature = 0.1,
    };

    [JsonPropertyName("text_quality")]
    public AiModelDefinition TextQuality { get; set; } = new()
    {
        Description = "Batch/scheduled tasks (ingestion manifest, vibe tags, disambiguation)",
        File = "llama-3.2-3b-instruct.Q4_K_M.gguf",
        DownloadUrl = "https://huggingface.co/bartowski/Llama-3.2-3B-Instruct-GGUF/resolve/main/Llama-3.2-3B-Instruct-Q4_K_M.gguf",
        SizeMB = 2000,
        ContextLength = 4096,
        MaxTokens = 512,
        Temperature = 0.1,
    };

    [JsonPropertyName("text_scholar")]
    public AiModelDefinition TextScholar { get; set; } = new()
    {
        Description = "Deep enrichment (description intelligence, complex analysis) — scheduled/overnight",
        File = "Meta-Llama-3.1-8B-Instruct-Q4_K_M.gguf",
        DownloadUrl = "https://huggingface.co/bartowski/Meta-Llama-3.1-8B-Instruct-GGUF/resolve/main/Meta-Llama-3.1-8B-Instruct-Q4_K_M.gguf",
        SizeMB = 4920,
        ContextLength = 8192,
        MaxTokens = 1024,
        Temperature = 0.1,
    };

    [JsonPropertyName("audio")]
    public AiModelDefinition Audio { get; set; } = new()
    {
        Description = "Whisper transcription and language detection",
        File = "ggml-medium.bin",
        DownloadUrl = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-medium.bin",
        SizeMB = 1500,
    };

    [JsonPropertyName("text_cjk")]
    public AiModelDefinition TextCjk { get; set; } = new()
    {
        Description = "Multilingual model with strong CJK support for Chinese, Japanese, and Korean content analysis",
        File = "qwen2.5-3b-instruct-q4_k_m.gguf",
        DownloadUrl = "https://huggingface.co/Qwen/Qwen2.5-3B-Instruct-GGUF/resolve/main/qwen2.5-3b-instruct-q4_k_m.gguf",
        SizeMB = 2048,
        ContextLength = 8192,
        MaxTokens = 512,
        Temperature = 0.1,
    };

    /// <summary>Get the definition for a given role.</summary>
    public AiModelDefinition GetByRole(Domain.Enums.AiModelRole role) => role switch
    {
        Domain.Enums.AiModelRole.TextFast    => TextFast,
        Domain.Enums.AiModelRole.TextQuality => TextQuality,
        Domain.Enums.AiModelRole.TextScholar => TextScholar,
        Domain.Enums.AiModelRole.Audio       => Audio,
        Domain.Enums.AiModelRole.TextCjk     => TextCjk,
        _ => throw new ArgumentOutOfRangeException(nameof(role)),
    };
}

/// <summary>Definition of a single AI model.</summary>
public sealed class AiModelDefinition
{
    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("file")]
    public string File { get; set; } = "";

    [JsonPropertyName("download_url")]
    public string DownloadUrl { get; set; } = "";

    [JsonPropertyName("sha256")]
    public string? Sha256 { get; set; }

    [JsonPropertyName("size_mb")]
    public int SizeMB { get; set; }

    [JsonPropertyName("context_length")]
    public int ContextLength { get; set; } = 2048;

    [JsonPropertyName("max_tokens")]
    public int MaxTokens { get; set; } = 256;

    [JsonPropertyName("temperature")]
    public double Temperature { get; set; } = 0.1;

    [JsonPropertyName("gpu_layers")]
    public int GpuLayers { get; set; }

    [JsonPropertyName("threads")]
    public int Threads { get; set; } = 4;

    [JsonPropertyName("language")]
    public string? Language { get; set; }

    [JsonPropertyName("translate")]
    public bool Translate { get; set; }
}

/// <summary>Per-feature enable/disable flags.</summary>
public sealed class AiFeatureFlags
{
    [JsonPropertyName("smart_labeling")]
    public bool SmartLabeling { get; set; } = true;

    [JsonPropertyName("type_logic")]
    public bool TypeLogic { get; set; } = true;

    [JsonPropertyName("audio_language_detection")]
    public bool AudioLanguageDetection { get; set; } = true;

    [JsonPropertyName("qid_disambiguation")]
    public bool QidDisambiguation { get; set; } = true;

    [JsonPropertyName("series_alignment")]
    public bool SeriesAlignment { get; set; } = true;

    [JsonPropertyName("watching_order")]
    public bool WatchingOrder { get; set; } = true;

    [JsonPropertyName("vibe_tags")]
    public bool VibeTags { get; set; } = true;

    [JsonPropertyName("tldr")]
    public bool Tldr { get; set; } = true;

    [JsonPropertyName("cover_art_validation")]
    public bool CoverArtValidation { get; set; } = true;

    [JsonPropertyName("whisper_alignment")]
    public bool WhisperAlignment { get; set; } = true;

    [JsonPropertyName("subtitle_sync")]
    public bool SubtitleSync { get; set; } = true;

    [JsonPropertyName("taste_profiling")]
    public bool TasteProfiling { get; set; } = true;

    [JsonPropertyName("why_factor")]
    public bool WhyFactor { get; set; } = true;

    [JsonPropertyName("intent_search")]
    public bool IntentSearch { get; set; } = true;

    [JsonPropertyName("url_paste")]
    public bool UrlPaste { get; set; } = true;

    [JsonPropertyName("description_intelligence")]
    public bool DescriptionIntelligence { get; set; } = true;
}

/// <summary>Per-category controlled vocabulary for vibe tagging.</summary>
public sealed class AiVibeVocabulary
{
    [JsonPropertyName("books")]
    public List<string> Books { get; set; } =
    [
        "page-turner", "dense-prose", "lyrical", "philosophical", "cozy",
        "atmospheric", "haunting", "cerebral", "whimsical", "epic",
        "intimate", "slow-burn", "provocative", "heartwarming", "dark"
    ];

    [JsonPropertyName("movies_tv")]
    public List<string> MoviesTv { get; set; } =
    [
        "visually-stunning", "slow-paced", "action-packed", "suspenseful", "atmospheric",
        "gritty", "dreamlike", "tense", "humorous", "nostalgic",
        "visceral", "epic", "intimate", "dark", "uplifting"
    ];

    [JsonPropertyName("music")]
    public List<string> Music { get; set; } =
    [
        "mellow", "heavy", "groovy", "upbeat", "melancholic",
        "ambient", "energetic", "dreamy", "aggressive", "soulful",
        "minimalist", "lush", "raw", "hypnotic", "anthemic"
    ];

    [JsonPropertyName("comics")]
    public List<string> Comics { get; set; } =
    [
        "gritty", "colorful", "noir", "whimsical", "epic",
        "dark", "action-packed", "intimate", "surreal", "classic"
    ];

    [JsonPropertyName("podcasts")]
    public List<string> Podcasts { get; set; } =
    [
        "conversational", "investigative", "educational", "comedic", "narrative",
        "thought-provoking", "casual", "deep-dive", "interview", "storytelling"
    ];

    /// <summary>Get vocabulary for a media category string.</summary>
    public IReadOnlyList<string> GetForCategory(string category) => category.ToLowerInvariant() switch
    {
        "books" or "audiobooks" => Books,
        "movies" or "tv" or "movies_tv" => MoviesTv,
        "music" => Music,
        "comics" => Comics,
        "podcasts" => Podcasts,
        _ => Books, // default fallback
    };
}

/// <summary>Scheduling configuration for background AI tasks.</summary>
public sealed class AiScheduling
{
    /// <summary>
    /// Number of hours within which a Whisper bake job may be deferred
    /// after the scheduled start time. Jobs triggered outside this window
    /// are skipped until the next scheduled run.
    /// </summary>
    [JsonPropertyName("whisper_bake_window_hours")]
    public int WhisperBakeWindowHours { get; set; } = 4;
}
