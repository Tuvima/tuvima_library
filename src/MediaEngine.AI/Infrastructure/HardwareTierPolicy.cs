namespace MediaEngine.AI.Infrastructure;

/// <summary>Classifies measured hardware; feature ownership remains in explicit feature flags.</summary>
public static class HardwareTierPolicy
{
    public const string TierHigh = "high";
    public const string TierMedium = "medium";
    public const string TierLow = "low";

    public static string ClassifyTier(
        double tokensPerSecond,
        long availableRamMb,
        bool hasDedicatedGpu,
        long gpuVramMb = 0)
    {
        if (hasDedicatedGpu && (gpuVramMb >= 8192 || availableRamMb >= 16384) && tokensPerSecond >= 15)
            return TierHigh;
        if (tokensPerSecond >= 60 && availableRamMb >= 16384)
            return TierHigh;
        if (tokensPerSecond >= 10 && availableRamMb >= 8192)
            return TierMedium;
        return TierLow;
    }

    public static int GetGpuLayerCount(string tier) => tier switch
    {
        TierHigh => 999,
        TierMedium => 16,
        _ => 0,
    };
}
