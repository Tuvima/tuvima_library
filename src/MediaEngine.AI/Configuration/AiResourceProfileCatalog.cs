namespace MediaEngine.AI.Configuration;

public static class AiResourceProfileNames
{
    public const string Essential = "essential";
    public const string Standard = "standard";
    public const string Advanced = "advanced";

    public static bool IsSupported(string? value) =>
        value is not null && (value.Equals(Essential, StringComparison.OrdinalIgnoreCase)
            || value.Equals(Standard, StringComparison.OrdinalIgnoreCase)
            || value.Equals(Advanced, StringComparison.OrdinalIgnoreCase));
}

/// <summary>Code-owned launch catalogue. Configuration selects a profile; it cannot redefine artifacts.</summary>
public static class AiResourceProfileCatalog
{
    public const string EssentialCatalogKey = "qwen3_0_6b_q8";
    public const string StandardCatalogKey = "qwen3_1_7b_q5";
    public const string AdvancedCatalogKey = "qwen3_4b_q4";
    public const string AudioCatalogKey = "whisper_medium";

    public static AiModelDefinitions CreateDefinitions(string? profile)
    {
        var text = CreateText(profile);
        return new AiModelDefinitions
        {
            TextFast = text.Clone(contextLength: Math.Min(text.ContextLength, 4096), maxTokens: 256),
            TextQuality = text.Clone(contextLength: Math.Min(text.ContextLength, 8192), maxTokens: 512),
            TextScholar = text.Clone(contextLength: text.ContextLength, maxTokens: 1024),
            TextCjk = text.Clone(contextLength: Math.Min(text.ContextLength, 8192), maxTokens: 512),
            Audio = new AiModelDefinition
            {
                CatalogKey = AudioCatalogKey,
                Description = "Optional Whisper transcription and alignment feature pack",
                File = "ggml-medium.bin",
                DownloadUrl = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-medium.bin",
                Sha256 = "6c14d5adee5f86394037b4e4e8b59f1673b6cee10e3cf0b11bbdbee79c156208",
                SizeMB = 1500,
                ContextLength = 2048,
                MaxTokens = 256,
                Temperature = 0,
                Threads = 4,
                Language = "auto",
            },
        };
    }

    public static AiModelDefinition CreateText(string? profile) =>
        (profile ?? AiResourceProfileNames.Standard).ToLowerInvariant() switch
        {
            AiResourceProfileNames.Essential => new AiModelDefinition
            {
                CatalogKey = EssentialCatalogKey,
                Description = "Essential local text profile",
                File = "Qwen3-0.6B-Q8_0.gguf",
                DownloadUrl = "https://huggingface.co/Qwen/Qwen3-0.6B-GGUF/resolve/main/Qwen3-0.6B-Q8_0.gguf",
                Sha256 = "9465e63a22add5354d9bb4b99e90117043c7124007664907259bd16d043bb031",
                SizeMB = 639,
                ContextLength = 4096,
                MaxTokens = 512,
                Temperature = 0.1,
                Threads = 4,
            },
            AiResourceProfileNames.Advanced => new AiModelDefinition
            {
                CatalogKey = AdvancedCatalogKey,
                Description = "Advanced local text profile",
                File = "Qwen3-4B-Q4_K_M.gguf",
                DownloadUrl = "https://huggingface.co/Qwen/Qwen3-4B-GGUF/resolve/main/Qwen3-4B-Q4_K_M.gguf",
                Sha256 = "7485fe6f11af29433bc51cab58009521f205840f5b4ae3a32fa7f92e8534fdf5",
                SizeMB = 2500,
                ContextLength = 16384,
                MaxTokens = 1024,
                Temperature = 0.1,
                Threads = 4,
            },
            _ => new AiModelDefinition
            {
                CatalogKey = StandardCatalogKey,
                Description = "Standard local text profile",
                File = "Qwen3-1.7B-Q5_K_M.gguf",
                DownloadUrl = "https://huggingface.co/unsloth/Qwen3-1.7B-GGUF/resolve/main/Qwen3-1.7B-Q5_K_M.gguf",
                Sha256 = "b0949de5b2e06cbed6aa96517f9bd8afb334584b6f95ee83479292ff4bdd8ed3",
                SizeMB = 1260,
                ContextLength = 8192,
                MaxTokens = 512,
                Temperature = 0.1,
                Threads = 4,
            },
        };

    private static AiModelDefinition Clone(this AiModelDefinition source, int contextLength, int maxTokens) => new()
    {
        CatalogKey = source.CatalogKey,
        Description = source.Description,
        File = source.File,
        DownloadUrl = source.DownloadUrl,
        Sha256 = source.Sha256,
        SizeMB = source.SizeMB,
        ContextLength = contextLength,
        MaxTokens = maxTokens,
        Temperature = source.Temperature,
        GpuLayers = source.GpuLayers,
        Threads = source.Threads,
        Language = source.Language,
        Translate = source.Translate,
    };
}
