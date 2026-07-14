using MediaEngine.AI.Configuration;
using MediaEngine.Domain.Enums;

namespace MediaEngine.AI.Infrastructure;

/// <summary>
/// Explains whether a configured model satisfies its role contract. This class
/// never loads a model; it is safe to use from configuration and status paths.
/// </summary>
public sealed class AiModelSelectionAdvisor
{
    private readonly AiSettings _settings;

    public AiModelSelectionAdvisor(AiSettings settings) => _settings = settings;

    public AiModelSelectionDecision GetDecision(AiModelRole role)
    {
        var roleKey = AiModelDefinitions.ToRoleKey(role);
        var definition = _settings.Models.GetByRole(role);
        var catalog = _settings.GetCatalogEntryForRole(role);
        return BuildDecision(roleKey, definition.CatalogKey, enabled: true, experimental: false,
            definition.ContextLength, 0, _settings.GetRequirementForRole(role), catalog);
    }

    public AiModelSelectionDecision GetDecision(string operationalRole)
    {
        if (!_settings.OperationalRoles.TryGetValue(operationalRole, out var role))
        {
            return BuildDecision(operationalRole, null, enabled: false, experimental: false,
                0, 0, GetRequirement(operationalRole), null,
                "Operational role is not configured.");
        }

        _settings.ModelCatalog.TryGetValue(role.CatalogKey, out var catalog);
        return BuildDecision(operationalRole, role.CatalogKey, role.Enabled, role.Experimental,
            role.MaxContextLength, role.MemoryEnvelopeMB, GetRequirement(operationalRole), catalog);
    }

    public IReadOnlyList<AiModelSelectionDecision> GetOperationalDecisions() =>
        _settings.OperationalRoles.Keys.Order(StringComparer.OrdinalIgnoreCase).Select(GetDecision).ToList();

    private AiRoleRequirement? GetRequirement(string key) =>
        _settings.RoleRequirements.TryGetValue(key, out var requirement) ? requirement : null;

    private static AiModelSelectionDecision BuildDecision(
        string role,
        string? catalogKey,
        bool enabled,
        bool experimental,
        int contextEnvelope,
        int memoryEnvelope,
        AiRoleRequirement? requirement,
        AiModelCatalogEntry? catalog,
        string? initialWarning = null)
    {
        var warnings = new List<string>();
        if (initialWarning is not null) warnings.Add(initialWarning);
        if (catalog is null) warnings.Add("Selected model is not present in the model catalog.");
        if (requirement is null) warnings.Add("No role requirement is configured.");

        if (catalog is not null && requirement is not null)
        {
            if (requirement.MaxDefaultSizeMB > 0 && catalog.SizeMB > requirement.MaxDefaultSizeMB)
                warnings.Add($"Selected model is {catalog.SizeMB} MB, above the role cap of {requirement.MaxDefaultSizeMB} MB.");
            if (memoryEnvelope > 0 && catalog.MemoryEnvelopeMB > memoryEnvelope)
                warnings.Add($"Model memory envelope ({catalog.MemoryEnvelopeMB} MB) exceeds the role envelope ({memoryEnvelope} MB).");
            if (contextEnvelope > 0 && catalog.MaxContextLength > 0 && contextEnvelope > catalog.MaxContextLength)
                warnings.Add($"Role context ({contextEnvelope}) exceeds the model maximum ({catalog.MaxContextLength}).");
            if (catalog.Experimental && !requirement.ExperimentalAllowed)
                warnings.Add("Experimental model is not permitted for this role.");
            if (experimental != catalog.Experimental)
                warnings.Add("Role and catalog experimental flags do not agree.");

            foreach (var capability in requirement.RequiredCapabilities.Where(c => !HasCapability(catalog.Capabilities, c)))
                warnings.Add($"Missing required capability: {capability}.");
        }

        if (catalog is not null)
        {
            if (!catalog.Readiness.ConfigurationReady) warnings.Add("Model configuration is not ready.");
            if (!catalog.Readiness.RuntimeReady) warnings.Add("A compatible local runtime is not available.");
            if (!catalog.Readiness.Validated) warnings.Add("The configured evaluation suite has not passed.");
            warnings.AddRange(catalog.Readiness.BlockingReasons.Where(reason => !warnings.Contains(reason, StringComparer.Ordinal)));
        }

        var canEnable = catalog is not null
            && requirement is not null
            && catalog.Readiness.ConfigurationReady
            && catalog.Readiness.RuntimeReady
            && !warnings.Any(w => w.StartsWith("Missing required capability", StringComparison.Ordinal)
                                  || w.Contains("exceeds", StringComparison.OrdinalIgnoreCase)
                                  || w.Contains("not permitted", StringComparison.OrdinalIgnoreCase));
        var status = !enabled ? "disabled" : !canEnable ? "blocked" : catalog?.Readiness.Validated == true ? "ready" : "needs_validation";

        return new(
            role, catalogKey, catalog?.DisplayName ?? catalogKey ?? "Unconfigured", catalog?.Family ?? "",
            catalog?.Provider ?? "", catalog?.License ?? "", catalog?.Runtime ?? "", status,
            catalog?.SelectionTier ?? "", requirement?.BenchmarkSuite ?? catalog?.Validation.BenchmarkSuite ?? "",
            catalog?.SelectionRationale ?? "No catalog rationale is available.", requirement?.Description ?? "", warnings,
            enabled, canEnable, catalog?.Experimental ?? experimental, catalog?.Quantization ?? "", catalog?.SizeMB ?? 0,
            memoryEnvelope > 0 ? memoryEnvelope : catalog?.MemoryEnvelopeMB ?? 0,
            contextEnvelope > 0 ? contextEnvelope : catalog?.MaxContextLength ?? 0,
            catalog?.SourceUrl ?? "", !string.IsNullOrWhiteSpace(catalog?.Sha256),
            catalog?.Readiness.ConfigurationReady ?? false, catalog?.Readiness.RuntimeReady ?? false,
            catalog?.Readiness.Validated ?? false, catalog?.Readiness.BlockingReasons ?? []);
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
        "experimental_multimodal" => capabilities.ExperimentalMultimodal,
        "embedding_output" => capabilities.EmbeddingOutput,
        "function_calling" => capabilities.FunctionCalling,
        "tool_calling" => capabilities.ToolCalling,
        _ => false,
    };
}

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
