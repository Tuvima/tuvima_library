namespace MediaEngine.AI.Infrastructure;

/// <summary>
/// Determines which AI features are enabled based on the detected hardware tier.
/// </summary>
public static class HardwareTierPolicy
{
    public const string TierHigh    = "high";
    public const string TierMedium  = "medium";
    public const string TierLow     = "low";
    public const string TierMinimal = "minimal";

    /// <summary>
    /// Classify hardware into a tier based on benchmark results.
    /// </summary>
    public static string ClassifyTier(double tokensPerSecond, long availableRamMb, bool gpuDetected)
    {
        if (tokensPerSecond >= 60 && gpuDetected && availableRamMb >= 8192)
            return TierHigh;
        if (tokensPerSecond >= 15 && availableRamMb >= 4096)
            return TierMedium;
        if (tokensPerSecond >= 5 && availableRamMb >= 2048)
            return TierLow;
        return TierMinimal;
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
            PreferredTextModel             = "text_quality",
            MaxGpuLayers                   = -1, // All layers
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
            MaxGpuLayers                   = 16,
        },
        TierLow => new FeatureTierResult
        {
            SmartLabelerEnabled            = true,
            MediaTypeAdvisorEnabled        = true,
            DescriptionIntelligenceEnabled = false,
            VibeTagsEnabled                = false,
            QidDisambiguationEnabled       = false,
            WhisperEnabled                 = false,
            EnrichmentMode                 = EnrichmentMode.Overnight,
            PreferredTextModel             = "text_fast",
            MaxGpuLayers                   = 0,
        },
        _ => new FeatureTierResult // Minimal
        {
            SmartLabelerEnabled            = false,
            MediaTypeAdvisorEnabled        = false,
            DescriptionIntelligenceEnabled = false,
            VibeTagsEnabled                = false,
            QidDisambiguationEnabled       = false,
            WhisperEnabled                 = false,
            EnrichmentMode                 = EnrichmentMode.Disabled,
            PreferredTextModel             = "none",
            MaxGpuLayers                   = 0,
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
    public int            MaxGpuLayers                   { get; set; }
}
