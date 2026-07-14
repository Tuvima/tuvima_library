using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using MediaEngine.Domain.Enums;

namespace MediaEngine.AI.Configuration;

public sealed record AiConfigurationError(string Path, string Message);

public sealed class AiConfigurationException(IReadOnlyList<AiConfigurationError> errors)
    : InvalidOperationException(BuildMessage(errors))
{
    public IReadOnlyList<AiConfigurationError> Errors { get; } = errors;

    private static string BuildMessage(IReadOnlyList<AiConfigurationError> errors) =>
        $"AI configuration is invalid: {string.Join("; ", errors.Select(error => $"{error.Path}: {error.Message}"))}";
}

/// <summary>
/// Central validation for AI runtime configuration. Catalog-only and disabled
/// experimental entries may remain not-ready; executable definitions must be safe.
/// </summary>
public static partial class AiSettingsValidator
{
    private static readonly HashSet<string> RuntimeKinds =
        new(["text", "audio", "embedding", "multimodal", "function"], StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<AiConfigurationError> Validate(AiSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var errors = new List<AiConfigurationError>();

        if (string.IsNullOrWhiteSpace(settings.ModelsDirectory))
        {
            Add("models_directory", "Models directory is required.");
        }
        else
        {
            ValidateDirectory(settings.ModelsDirectory, "models_directory", errors);
        }

        if (settings.IdleUnloadSeconds <= 0)
        {
            Add("idle_unload_seconds", "Must be positive.");
        }

        if (settings.InferenceTimeoutSeconds <= 0)
        {
            Add("inference_timeout_seconds", "Must be positive.");
        }

        if (settings.MaxConcurrentInferences != 1)
        {
            Add("max_concurrent_inferences", "Must be 1 while the runtime supports one resident model.");
        }

        if (settings.MinimumFreeDiskMB < 256)
        {
            Add("minimum_free_disk_mb", "Must retain at least 256 MB.");
        }

        if (settings.EnrichmentBatchSize <= 0)
        {
            Add("enrichment_batch_size", "Must be positive.");
        }

        if (settings.Models is null)
        {
            Add("models", "Model definitions are required.");
        }
        else
        {
            foreach (var role in Enum.GetValues<AiModelRole>())
            {
                ValidateExecutableModel(settings.Models.GetByRole(role), $"models.{AiModelDefinitions.ToRoleKey(role)}", errors);
            }

            ValidateSharedArtifacts(settings.Models, errors);
        }

        var catalog = settings.ModelCatalog ?? new Dictionary<string, AiModelCatalogEntry>();
        foreach (var (key, entry) in catalog)
        {
            var path = $"model_catalog.{key}";
            if (string.IsNullOrWhiteSpace(key))
            {
                Add("model_catalog", "Catalog keys cannot be blank.");
            }

            if (!string.IsNullOrWhiteSpace(entry.File))
            {
                ValidateFileName(entry.File, $"{path}.file", errors);
            }

            ValidateOptionalHttpsUri(entry.DownloadUrl, $"{path}.download_url", errors);
            ValidateOptionalHttpsUri(entry.SourceUrl, $"{path}.source_url", errors);
            ValidateOptionalChecksum(entry.Sha256, $"{path}.sha256", errors);
            if (entry.SizeMB < 0)
            {
                Add($"{path}.size_mb", "Cannot be negative.");
            }

            if (entry.MemoryEnvelopeMB < 0)
            {
                Add($"{path}.memory_envelope_mb", "Cannot be negative.");
            }

            if (entry.MaxContextLength < 0)
            {
                Add($"{path}.max_context_length", "Cannot be negative.");
            }
        }

        foreach (var (key, role) in settings.OperationalRoles ?? new Dictionary<string, AiOperationalRoleDefinition>())
        {
            var path = $"operational_roles.{key}";
            if (string.IsNullOrWhiteSpace(key))
            {
                Add("operational_roles", "Role keys cannot be blank.");
            }

            if (!RuntimeKinds.Contains(role.RuntimeKind))
            {
                Add($"{path}.runtime_kind", "Must be text, audio, embedding, multimodal, or function.");
            }

            if (role.MaxConcurrency != 1)
            {
                Add($"{path}.max_concurrency", "Must be 1 while the runtime supports one resident model.");
            }

            if (role.MemoryEnvelopeMB < 0)
            {
                Add($"{path}.memory_envelope_mb", "Cannot be negative.");
            }

            if (role.MaxContextLength < 0)
            {
                Add($"{path}.max_context_length", "Cannot be negative.");
            }

            if (role.MaxOutputTokens < 0)
            {
                Add($"{path}.max_output_tokens", "Cannot be negative.");
            }

            if (role.Temperature is < 0 or > 2)
            {
                Add($"{path}.temperature", "Must be between 0 and 2.");
            }

            if (role.Enabled)
            {
                if (string.IsNullOrWhiteSpace(role.CatalogKey))
                {
                    Add($"{path}.catalog_key", "Enabled roles require a catalog key.");
                }
                else if (!catalog.ContainsKey(role.CatalogKey))
                {
                    Add($"{path}.catalog_key", "References an unknown catalog entry.");
                }
            }
        }

        return errors;

        void Add(string path, string message) => errors.Add(new AiConfigurationError(path, message));
    }

    public static void ValidateAndThrow(AiSettings settings)
    {
        var errors = Validate(settings);
        if (errors.Count > 0)
        {
            throw new AiConfigurationException(errors);
        }
    }

    internal static bool IsSafeFileName(string fileName) =>
        !string.IsNullOrWhiteSpace(fileName)
        && !Path.IsPathRooted(fileName)
        && fileName is not "." and not ".."
        && string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal)
        && fileName.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;

    private static void ValidateExecutableModel(
        AiModelDefinition model,
        string path,
        List<AiConfigurationError> errors)
    {
        if (model is null)
        {
            errors.Add(new AiConfigurationError(path, "Definition is required."));
            return;
        }

        ValidateFileName(model.File, $"{path}.file", errors);
        ValidateOptionalHttpsUri(model.DownloadUrl, $"{path}.download_url", errors);
        ValidateOptionalChecksum(model.Sha256, $"{path}.sha256", errors);
        if (model.SizeMB <= 0)
        {
            errors.Add(new AiConfigurationError($"{path}.size_mb", "Must be positive."));
        }

        if (model.ContextLength <= 0)
        {
            errors.Add(new AiConfigurationError($"{path}.context_length", "Must be positive."));
        }

        if (model.MaxTokens <= 0)
        {
            errors.Add(new AiConfigurationError($"{path}.max_tokens", "Must be positive."));
        }

        if (model.Temperature is < 0 or > 2)
        {
            errors.Add(new AiConfigurationError($"{path}.temperature", "Must be between 0 and 2."));
        }

        if (model.GpuLayers < 0)
        {
            errors.Add(new AiConfigurationError($"{path}.gpu_layers", "Cannot be negative."));
        }

        if (model.Threads <= 0)
        {
            errors.Add(new AiConfigurationError($"{path}.threads", "Must be positive."));
        }
    }

    private static void ValidateSharedArtifacts(
        AiModelDefinitions models,
        List<AiConfigurationError> errors)
    {
        var definitions = Enum.GetValues<AiModelRole>()
            .Select(role => new
            {
                Role = role,
                Definition = models.GetByRole(role),
                Artifact = $"{(role == AiModelRole.Audio ? "whisper" : "llama")}/{models.GetByRole(role).File}",
            })
            .GroupBy(item => item.Artifact, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1);

        foreach (var group in definitions)
        {
            var expected = group.First();
            foreach (var item in group.Skip(1))
            {
                var path = $"models.{AiModelDefinitions.ToRoleKey(item.Role)}";
                if (!string.Equals(
                        expected.Definition.Sha256,
                        item.Definition.Sha256,
                        StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add(new AiConfigurationError(
                        $"{path}.sha256",
                        $"Must match the checksum for shared artifact {group.Key}."));
                }

                if (!string.Equals(
                        expected.Definition.DownloadUrl,
                        item.Definition.DownloadUrl,
                        StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add(new AiConfigurationError(
                        $"{path}.download_url",
                        $"Must match the download URL for shared artifact {group.Key}."));
                }

                if (expected.Definition.SizeMB != item.Definition.SizeMB)
                {
                    errors.Add(new AiConfigurationError(
                        $"{path}.size_mb",
                        $"Must match the size for shared artifact {group.Key}."));
                }
            }
        }
    }

    private static void ValidateDirectory(string directory, string path, List<AiConfigurationError> errors)
    {
        try
        {
            _ = Path.GetFullPath(directory);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            errors.Add(new AiConfigurationError(path, "Must be a valid filesystem path."));
        }
    }

    private static void ValidateFileName(string file, string path, List<AiConfigurationError> errors)
    {
        if (!IsSafeFileName(file))
        {
            errors.Add(new AiConfigurationError(path, "Must be a simple file name without path traversal."));
        }
    }

    private static void ValidateOptionalHttpsUri(string? value, string path, List<AiConfigurationError> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            errors.Add(new AiConfigurationError(path, "Must be an absolute HTTPS URI."));
        }
    }

    private static void ValidateOptionalChecksum(string? value, string path, List<AiConfigurationError> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (!Sha256Regex().IsMatch(value))
        {
            errors.Add(new AiConfigurationError(path, "Must contain exactly 64 hexadecimal SHA-256 characters."));
        }
    }

    [GeneratedRegex("^[A-Fa-f0-9]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Regex();
}

/// <summary>Immutable runtime copy detached from mutable configuration binding objects.</summary>
public sealed class AiRuntimeSettingsSnapshot
{
    private AiRuntimeSettingsSnapshot(
        string modelsDirectory,
        int idleUnloadSeconds,
        int inferenceTimeoutSeconds,
        int maxConcurrentInferences,
        int minimumFreeDiskMb,
        IReadOnlyDictionary<AiModelRole, AiModelRuntimeDefinition> models)
    {
        ModelsDirectory = modelsDirectory;
        IdleUnloadSeconds = idleUnloadSeconds;
        InferenceTimeoutSeconds = inferenceTimeoutSeconds;
        MaxConcurrentInferences = maxConcurrentInferences;
        MinimumFreeDiskMB = minimumFreeDiskMb;
        Models = models;
    }

    public string ModelsDirectory { get; }
    public int IdleUnloadSeconds { get; }
    public int InferenceTimeoutSeconds { get; }
    public int MaxConcurrentInferences { get; }
    public int MinimumFreeDiskMB { get; }
    public IReadOnlyDictionary<AiModelRole, AiModelRuntimeDefinition> Models { get; }

    public AiModelRuntimeDefinition GetModel(AiModelRole role) => Models[role];

    public static AiRuntimeSettingsSnapshot Create(AiSettings settings)
    {
        AiSettingsValidator.ValidateAndThrow(settings);
        var models = Enum.GetValues<AiModelRole>().ToDictionary(
            role => role,
            role => AiModelRuntimeDefinition.From(settings.Models.GetByRole(role)));

        return new AiRuntimeSettingsSnapshot(
            Path.GetFullPath(settings.ModelsDirectory),
            settings.IdleUnloadSeconds,
            settings.InferenceTimeoutSeconds,
            settings.MaxConcurrentInferences,
            settings.MinimumFreeDiskMB,
            new ReadOnlyDictionary<AiModelRole, AiModelRuntimeDefinition>(models));
    }
}

public sealed record AiModelRuntimeDefinition(
    string? CatalogKey,
    string Description,
    string File,
    Uri? DownloadUri,
    string? Sha256,
    int SizeMB,
    int ContextLength,
    int MaxTokens,
    double Temperature,
    int GpuLayers,
    int Threads,
    string? Language,
    bool Translate)
{
    internal static AiModelRuntimeDefinition From(AiModelDefinition model) => new(
        model.CatalogKey,
        model.Description,
        model.File,
        string.IsNullOrWhiteSpace(model.DownloadUrl) ? null : new Uri(model.DownloadUrl, UriKind.Absolute),
        string.IsNullOrWhiteSpace(model.Sha256) ? null : model.Sha256.ToLowerInvariant(),
        model.SizeMB,
        model.ContextLength,
        model.MaxTokens,
        model.Temperature,
        model.GpuLayers,
        model.Threads,
        model.Language,
        model.Translate);
}
