namespace MediaEngine.AI.Infrastructure;

/// <summary>
/// Determines which AI features are enabled based on the detected hardware tier.
/// </summary>
public static class HardwareTierPolicy
{
    public const string TierHigh   = "high";
    public const string TierMedium = "medium";
    public const string TierLow    = "low";

    /// <summary>
    /// Classify hardware into a tier based on benchmark results.
    /// </summary>
    /// <param name="tokensPerSecond">Measured inference speed from benchmark.</param>
    /// <param name="availableRamMb">System RAM available (MB).</param>
    /// <param name="hasDedicatedGpu">True when a dedicated GPU was detected (not integrated). Intel iGPUs do not qualify.</param>
    /// <param name="gpuVramMb">GPU VRAM in MB (0 if unknown or no GPU).</param>
    public static string ClassifyTier(double tokensPerSecond, long availableRamMb, bool hasDedicatedGpu, long gpuVramMb = 0)
    {
        // High: dedicated GPU with ≥8GB VRAM, or dedicated GPU + ≥16GB RAM + decent tok/s.
        if (hasDedicatedGpu && (gpuVramMb >= 8192 || availableRamMb >= 16384) && tokensPerSecond >= 15)
            return TierHigh;

        // High: no GPU but very fast CPU with lots of RAM.
        if (tokensPerSecond >= 60 && availableRamMb >= 16384)
            return TierHigh;

        // Medium: decent CPU + sufficient RAM (or dedicated GPU with less VRAM).
        if (tokensPerSecond >= 10 && availableRamMb >= 8192)
            return TierMedium;

        // Low: everything else that can at least run the 1B model.
        return TierLow;
    }

    /// <summary>
    /// Get the feature configuration for a given tier.
    /// </summary>
    public static FeatureTierResult GetFeatures(string tier) => tier switch
    {
        TierHigh => new FeatureTierResult
        {
            SmartLabelerEnabled            = true,
            MediaTypeAdvisorEnabled        = true,
            DescriptionIntelligenceEnabled = true,
            VibeTagsEnabled                = true,
            QidDisambiguationEnabled       = true,
            WhisperEnabled                 = true,
            EnrichmentMode                 = EnrichmentMode.Continuous,
            PreferredTextModel             = "text_scholar",
            IngestionModel                 = "text_quality",
            InstantModel                   = "text_fast",
            EnrichmentModel                = "text_scholar",
            MaxGpuLayers                   = -1, // All layers
            ScholarAvailable               = true,
            CjkAvailable                   = true,
        },
        TierMedium => new FeatureTierResult
        {
            SmartLabelerEnabled            = true,
            MediaTypeAdvisorEnabled        = true,
            DescriptionIntelligenceEnabled = true,
            VibeTagsEnabled                = true,
            QidDisambiguationEnabled       = true,
            WhisperEnabled                 = true,
            EnrichmentMode                 = EnrichmentMode.Scheduled,
            PreferredTextModel             = "text_quality",
            IngestionModel                 = "text_quality",
            InstantModel                   = "text_fast",
            EnrichmentModel                = "text_scholar", // 8B on CPU — 3B fails 50% on complex DI grammar
            MaxGpuLayers                   = 16,
            ScholarAvailable               = true,
            CjkAvailable                   = true,
        },
        TierLow => new FeatureTierResult
        {
            SmartLabelerEnabled            = true,
            MediaTypeAdvisorEnabled        = true,
            DescriptionIntelligenceEnabled = true,
            VibeTagsEnabled                = true,  // Uses 3B (already loaded for SmartLabeler), ~3s per file
            QidDisambiguationEnabled       = true,  // Uses 3B, critical for correct Wikidata matching
            WhisperEnabled                 = false, // 1.5GB model + minutes per file — too heavy for low tier
            EnrichmentMode                 = EnrichmentMode.Overnight,
            PreferredTextModel             = "text_quality",
            IngestionModel                 = "text_fast",
            InstantModel                   = "text_fast",
            EnrichmentModel                = "text_scholar", // 8B on CPU overnight — 3B fails 50% on complex DI grammar
            MaxGpuLayers                   = 0,
            ScholarAvailable               = true,  // 8B runs on CPU (slower but reliable)
            CjkAvailable                   = false,
        },
        _ => new FeatureTierResult // Unknown / auto fallback — treat as Low
        {
            SmartLabelerEnabled            = true,
            MediaTypeAdvisorEnabled        = true,
            DescriptionIntelligenceEnabled = false,
            VibeTagsEnabled                = false,
            QidDisambiguationEnabled       = false,
            WhisperEnabled                 = false,
            EnrichmentMode                 = EnrichmentMode.Overnight,
            PreferredTextModel             = "text_fast",
            IngestionModel                 = "text_fast",
            InstantModel                   = "text_fast",
            EnrichmentModel                = "text_fast",
            MaxGpuLayers                   = 0,
            ScholarAvailable               = false,
            CjkAvailable                   = false,
        },
    };
}

public enum EnrichmentMode
{
    Continuous,
    Scheduled,
    Overnight,
    Disabled,
}

public sealed class FeatureTierResult
{
    public bool           SmartLabelerEnabled            { get; set; }
    public bool           MediaTypeAdvisorEnabled        { get; set; }
    public bool           DescriptionIntelligenceEnabled { get; set; }
    public bool           VibeTagsEnabled                { get; set; }
    public bool           QidDisambiguationEnabled       { get; set; }
    public bool           WhisperEnabled                 { get; set; }
    public EnrichmentMode EnrichmentMode                 { get; set; }
    public string         PreferredTextModel             { get; set; } = "text_quality";
    public string         InstantModel                   { get; set; } = "text_fast";
    public string         IngestionModel                 { get; set; } = "text_quality";
    public string         EnrichmentModel                { get; set; } = "text_quality";
    public int            MaxGpuLayers                   { get; set; }
    public bool           ScholarAvailable               { get; set; }
    /// <summary>True when the hardware can run the CJK model (Qwen 2.5 3B — same tier threshold as text_quality).</summary>
    public bool           CjkAvailable                   { get; set; }
}
