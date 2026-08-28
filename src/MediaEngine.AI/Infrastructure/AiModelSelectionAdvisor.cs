using MediaEngine.AI.Configuration;
using MediaEngine.Domain.Enums;

namespace MediaEngine.AI.Infrastructure;

/// <summary>Produces the one truthful runtime plan for the configured machine.</summary>
public sealed class AiModelSelectionAdvisor
{
    private readonly AiSettings _settings;

    public AiModelSelectionAdvisor(AiSettings settings) => _settings = settings;

    public AiExecutionPlan GetExecutionPlan()
    {
        var configured = Normalize(_settings.ResourceProfile);
        var recommended = GetRecommendedProfile();
        var constrained = GetRecommendedProfile() == AiResourceProfileNames.Essential;
        var advancedBlocked = configured == AiResourceProfileNames.Advanced
            && !_settings.HardwareProfile.AdvancedEligible;
        var effective = constrained ? AiResourceProfileNames.Essential : advancedBlocked ? recommended : configured;
        if (effective == AiResourceProfileNames.Advanced && !_settings.HardwareProfile.AdvancedEligible)
            effective = AiResourceProfileNames.Standard;

        var model = AiResourceProfileCatalog.CreateText(effective);
        return new AiExecutionPlan(
            configured,
            effective,
            recommended,
            model.CatalogKey ?? "",
            _settings.AudioPackEnabled,
            !advancedBlocked,
            advancedBlocked
                ? ["Advanced requires a successful current-machine benchmark meeting its throughput and memory gate."]
                : []);
    }

    public string GetRecommendedProfile()
    {
        if (_settings.HardwareProfile.AdvancedEligible)
            return AiResourceProfileNames.Advanced;

        var ram = _settings.HardwareProfile.AvailableRamMb;
        if (ram <= 0)
            ram = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024 * 1024);
        return ram < 8192 ? AiResourceProfileNames.Essential : AiResourceProfileNames.Standard;
    }

    public AiModelSelectionDecision GetDecision(AiModelRole role)
    {
        var definition = _settings.Models.GetByRole(role);
        var catalog = _settings.GetCatalogEntryForRole(role);
        var plan = GetExecutionPlan();
        var isAudio = role == AiModelRole.Audio;
        var enabled = !isAudio || plan.AudioPackEnabled;
        var warnings = new List<string>();
        if (isAudio && !plan.AudioPackEnabled)
            warnings.Add("The optional Whisper feature pack is disabled.");
        warnings.AddRange(plan.BlockingReasons);

        var canEnable = enabled && catalog is not null
            && catalog.Readiness.ConfigurationReady
            && catalog.Readiness.RuntimeReady
            && (!string.Equals(_settings.ResourceProfile, AiResourceProfileNames.Advanced, StringComparison.OrdinalIgnoreCase)
                || _settings.HardwareProfile.AdvancedEligible);
        var status = !enabled ? "disabled" : canEnable ? "ready" : "blocked";

        return new AiModelSelectionDecision(
            AiModelDefinitions.ToRoleKey(role),
            definition.CatalogKey,
            catalog?.DisplayName ?? definition.CatalogKey ?? "Unconfigured",
            catalog?.Family ?? "",
            catalog?.Provider ?? "",
            catalog?.License ?? "",
            catalog?.Runtime ?? "",
            status,
            catalog?.SelectionTier ?? "",
            catalog?.Validation.BenchmarkSuite ?? "",
            catalog?.SelectionRationale ?? "",
            isAudio ? "Optional audio feature pack" : $"{plan.EffectiveProfile} text profile",
            warnings,
            enabled,
            canEnable,
            false,
            catalog?.Quantization ?? "",
            catalog?.SizeMB ?? definition.SizeMB,
            catalog?.MemoryEnvelopeMB ?? definition.SizeMB,
            definition.ContextLength,
            catalog?.SourceUrl ?? "",
            !string.IsNullOrWhiteSpace(catalog?.Sha256),
            catalog?.Readiness.ConfigurationReady ?? false,
            catalog?.Readiness.RuntimeReady ?? false,
            canEnable,
            warnings);
    }

    public static bool HasCapability(AiModelCapabilities capabilities, string capability) => capability.ToLowerInvariant() switch
    {
        "text_input" => capabilities.TextInput,
        "audio_input" => capabilities.AudioInput,
        "image_input" => capabilities.ImageInput,
        "text_output" => capabilities.TextOutput,
        "structured_json" => capabilities.StructuredJson,
        "gbnf" => capabilities.Gbnf,
        "timestamp_segments" => capabilities.TimestampSegments,
        "word_timestamps" => capabilities.WordTimestamps,
        "sync_grade" => capabilities.SyncGrade,
        "multilingual" => capabilities.Multilingual,
        "cjk" => capabilities.Cjk,
        _ => false,
    };

    private static string Normalize(string? profile) => profile?.ToLowerInvariant() switch
    {
        AiResourceProfileNames.Essential => AiResourceProfileNames.Essential,
        AiResourceProfileNames.Advanced => AiResourceProfileNames.Advanced,
        _ => AiResourceProfileNames.Standard,
    };
}

public sealed record AiExecutionPlan(
    string ConfiguredProfile,
    string EffectiveProfile,
    string RecommendedProfile,
    string TextCatalogKey,
    bool AudioPackEnabled,
    bool ConfiguredProfileEligible,
    IReadOnlyList<string> BlockingReasons);

public sealed record AiModelSelectionDecision(
    string Role,
    string? CatalogKey,
    string DisplayName,
    string Family,
    string Provider,
    string License,
    string Runtime,
    string Status,
    string SelectionTier,
    string BenchmarkSuite,
    string Rationale,
    string Requirement,
    IReadOnlyList<string> Warnings,
    bool Enabled,
    bool CanEnable,
    bool Experimental,
    string Quantization,
    int SizeMB,
    int MemoryEnvelopeMB,
    int MaxContextLength,
    string SourceUrl,
    bool ChecksumConfigured,
    bool ConfigurationReady,
    bool RuntimeReady,
    bool Validated,
    IReadOnlyList<string> BlockingReasons);
