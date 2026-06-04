using MediaEngine.AI.Configuration;
using MediaEngine.Domain.Enums;

namespace MediaEngine.AI.Infrastructure;

public sealed class AiModelSelectionAdvisor
{
    private readonly AiSettings _settings;

    public AiModelSelectionAdvisor(AiSettings settings)
    {
        _settings = settings;
    }

    public AiModelSelectionDecision GetDecision(AiModelRole role)
    {
        var definition = _settings.Models.GetByRole(role);
        var requirement = _settings.GetRequirementForRole(role);
        var catalog = _settings.GetCatalogEntryForRole(role);
        var warnings = new List<string>();

        if (catalog is null)
        {
            warnings.Add("Selected model is not present in the model catalog.");
        }

        if (requirement is null)
        {
            warnings.Add("No role requirement is configured.");
        }

        if (catalog is not null && requirement is not null)
        {
            if (requirement.MaxDefaultSizeMB > 0 && catalog.SizeMB > requirement.MaxDefaultSizeMB)
            {
                warnings.Add($"Selected model is {catalog.SizeMB} MB, above the role default cap of {requirement.MaxDefaultSizeMB} MB.");
            }

            foreach (var capability in requirement.RequiredCapabilities)
            {
                if (!HasCapability(catalog.Capabilities, capability))
                    warnings.Add($"Missing required capability: {capability}.");
            }

            if (string.Equals(catalog.Status, "escalation", StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add("Escalation model selected; this should require a benchmark failure from smaller candidates.");
            }

            if (role == AiModelRole.Audio
                && !catalog.Capabilities.SyncGrade)
            {
                warnings.Add("Audio model is not sync-grade; it cannot replace Whisper for text sync.");
            }
        }

        var status = warnings.Count == 0 ? "configured" : "needs_validation";
        var rationale = catalog?.SelectionRationale
            ?? "Legacy role definition without catalog metadata.";

        return new AiModelSelectionDecision(
            Role: AiModelDefinitions.ToRoleKey(role),
            CatalogKey: definition.CatalogKey,
            DisplayName: catalog?.DisplayName ?? definition.File,
            Family: catalog?.Family ?? "",
            Provider: catalog?.Provider ?? "",
            License: catalog?.License ?? "",
            Runtime: catalog?.Runtime ?? "",
            Status: status,
            SelectionTier: catalog?.SelectionTier ?? "",
            BenchmarkSuite: requirement?.BenchmarkSuite ?? catalog?.Validation.BenchmarkSuite ?? "",
            Rationale: rationale,
            Requirement: requirement?.Description ?? "",
            Warnings: warnings);
    }

    private static bool HasCapability(AiModelCapabilities capabilities, string capability) =>
        capability.ToLowerInvariant() switch
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
    IReadOnlyList<string> Warnings);
