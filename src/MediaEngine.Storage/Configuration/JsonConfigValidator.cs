using System.Text.RegularExpressions;
using MediaEngine.Domain;
using MediaEngine.Domain.Models;
using MediaEngine.Storage.Models;

namespace MediaEngine.Storage.Configuration;

public static class JsonConfigValidator
{
    private static readonly Regex HexColorRegex = new("^#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$", RegexOptions.Compiled);

    public static IReadOnlyList<string> Validate<T>(T config, string relativePath)
        where T : class
    {
        var errors = new List<string>();

        switch (config)
        {
            case CoreConfiguration core:
                ValidateCore(core, errors);
                break;
            case ProviderConfiguration provider:
                ValidateProvider(provider, relativePath, errors);
                break;
            case HydrationSettings hydration:
                AddPositive(errors, hydration.StageConcurrency, "stage_concurrency");
                AddPositive(errors, hydration.Stage1TimeoutSeconds, "stage1_timeout_seconds");
                AddPositive(errors, hydration.Stage2TimeoutSeconds, "stage2_timeout_seconds");
                AddPositive(errors, hydration.Stage3TimeoutSeconds, "stage3_timeout_seconds");
                AddRange(errors, hydration.AutoReviewConfidenceThreshold, "auto_review_confidence_threshold", 0, 1);
                AddRange(errors, hydration.RetailAutoAcceptThreshold, "retail_auto_accept_threshold", 0, 1);
                AddRange(errors, hydration.RetailAmbiguousThreshold, "retail_ambiguous_threshold", 0, 1);
                break;
            case ScoringSettings scoring:
                AddRange(errors, scoring.AutoLinkThreshold, "auto_link_threshold", 0, 1);
                AddRange(errors, scoring.ConflictThreshold, "conflict_threshold", 0, 1);
                AddRange(errors, scoring.ConflictEpsilon, "conflict_epsilon", 0, 1);
                AddPositive(errors, scoring.StaleClaimDecayDays, "stale_claim_decay_days");
                AddRange(errors, scoring.StaleClaimDecayFactor, "stale_claim_decay_factor", 0, 1);
                break;
            case MaintenanceSettings maintenance:
                AddPositive(errors, maintenance.ActivityRetentionDays, "activity_retention_days");
                AddPositive(errors, maintenance.MaxTransactionLogEntries, "max_transaction_log_entries");
                AddPositive(errors, maintenance.WeeklySyncIntervalDays, "weekly_sync_interval_days");
                AddPositive(errors, maintenance.WeeklySyncBatchSize, "weekly_sync_batch_size");
                break;
            case MediaTypeConfiguration mediaTypes:
                ValidateMediaTypes(mediaTypes, errors);
                break;
            case Dictionary<string, MediaTypePipeline> pipelines:
                ValidatePipelines(pipelines, errors);
                break;
            case PaletteConfiguration palette:
                ValidatePalette(palette, errors);
                break;
        }

        return errors;
    }

    private static void ValidateCore(CoreConfiguration core, List<string> errors)
    {
        AddRequired(errors, core.SchemaVersion, "schema_version");
        AddRequired(errors, core.DatabasePath, "database_path");
        AddRequired(errors, core.ServerName, "server_name");
        if (!string.IsNullOrWhiteSpace(core.Country) && core.Country.Length != 2)
            errors.Add("country must be a two-letter country code.");
        if (!Allowed(core.DateFormat, "system", "short", "medium", "long", "iso8601"))
            errors.Add("date_format must be one of system, short, medium, long, iso8601.");
        if (!Allowed(core.TimeFormat, "system", "12h", "24h"))
            errors.Add("time_format must be one of system, 12h, 24h.");
        AddPositive(errors, core.Pipeline.LeaseSizes.Retail, "pipeline.lease_sizes.retail");
        AddPositive(errors, core.Pipeline.LeaseSizes.Wikidata, "pipeline.lease_sizes.wikidata");
        AddPositive(errors, core.Pipeline.LeaseSizes.Hydration, "pipeline.lease_sizes.hydration");
        AddPositive(errors, core.Pipeline.BatchGate.TimeoutSeconds, "pipeline.batch_gate.timeout_seconds");
    }

    private static void ValidateProvider(ProviderConfiguration provider, string relativePath, List<string> errors)
    {
        AddRequired(errors, provider.Name, "name");
        if (!string.IsNullOrWhiteSpace(provider.Name))
        {
            var fileName = Path.GetFileNameWithoutExtension(relativePath);
            if (!string.Equals(fileName, provider.Name, StringComparison.OrdinalIgnoreCase))
                errors.Add("name must match the provider config filename.");
        }

        AddRange(errors, provider.Weight, "weight", 0, 1);
        AddPositiveOrZero(errors, provider.ThrottleMs, "throttle_ms");
        AddPositive(errors, provider.MaxConcurrency, "max_concurrency");
        foreach (var stage in provider.HydrationStages)
        {
            if (stage is < 1 or > 3)
                errors.Add("hydration_stages values must be 1, 2, or 3.");
        }

        if (provider.HttpClient is not null)
            AddPositive(errors, provider.HttpClient.TimeoutSeconds, "http_client.timeout_seconds");
    }

    private static void ValidateMediaTypes(MediaTypeConfiguration mediaTypes, List<string> errors)
    {
        AddRequired(errors, mediaTypes.Version, "version");
        if (mediaTypes.Types.Count == 0)
            errors.Add("types must contain at least one media type.");

        foreach (var type in mediaTypes.Types)
        {
            AddRequired(errors, type.Key, "types[].key");
            AddRequired(errors, type.DisplayName, "types[].display_name");
            AddRequired(errors, type.CategoryFolder, "types[].category_folder");
            if (type.Extensions.Any(extension => !extension.StartsWith('.')))
                errors.Add("types[].extensions values must start with '.'.");
        }
    }

    private static void ValidatePipelines(Dictionary<string, MediaTypePipeline> pipelines, List<string> errors)
    {
        foreach (var (mediaType, pipeline) in pipelines)
        {
            AddRequired(errors, mediaType, "pipeline media type key");
            var ranks = new HashSet<int>();
            foreach (var provider in pipeline.Providers)
            {
                AddPositive(errors, provider.Rank, $"{mediaType}.providers[].rank");
                AddRequired(errors, provider.Name, $"{mediaType}.providers[].name");
                if (!ranks.Add(provider.Rank))
                    errors.Add($"{mediaType}.providers rank values must be unique.");
            }
        }
    }

    private static void ValidatePalette(PaletteConfiguration palette, List<string> errors)
    {
        ValidateColorMap(palette.Theme, "theme", errors);
        ValidateColorMap(palette.Status, "status", errors);
        ValidateColorMap(palette.Pipeline, "pipeline", errors);
        ValidateColorMap(palette.MediaType, "media_type", errors);
        ValidateColorMap(palette.Confidence, "confidence", errors);
        ValidateColorMap(palette.ReviewTrigger, "review_trigger", errors);
    }

    private static void ValidateColorMap(object colors, string section, List<string> errors)
    {
        foreach (var property in colors.GetType().GetProperties())
        {
            if (property.GetValue(colors) is not string value)
                continue;

            var name = property.Name;
            if (string.IsNullOrWhiteSpace(value))
                errors.Add($"{section}.{name} must not be empty.");
            else if (!value.StartsWith("rgba(", StringComparison.OrdinalIgnoreCase) && !HexColorRegex.IsMatch(value))
                errors.Add($"{section}.{name} must be a hex color or rgba() value.");
        }
    }

    private static bool Allowed(string value, params string[] allowed) =>
        allowed.Contains(value, StringComparer.OrdinalIgnoreCase);

    private static void AddRequired(List<string> errors, string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
            errors.Add($"{field} is required.");
    }

    private static void AddPositive(List<string> errors, int value, string field)
    {
        if (value <= 0)
            errors.Add($"{field} must be greater than 0.");
    }

    private static void AddPositiveOrZero(List<string> errors, int value, string field)
    {
        if (value < 0)
            errors.Add($"{field} must be 0 or greater.");
    }

    private static void AddRange(List<string> errors, double value, string field, double min, double max)
    {
        if (value < min || value > max)
            errors.Add($"{field} must be between {min} and {max}.");
    }
}
