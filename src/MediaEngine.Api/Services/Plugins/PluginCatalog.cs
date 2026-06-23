using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using MediaEngine.Plugins;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Api.Services.Plugins;

public sealed class PluginCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    private static readonly JsonDocumentOptions JsonDocumentOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    private static readonly JsonSerializerOptions WriteJsonOptions = new(JsonOptions)
    {
        WriteIndented = true,
    };

    private readonly IReadOnlyList<ITuvimaPlugin> _builtInPlugins;
    private readonly PluginSettingsStore _settingsStore;
    private readonly ILogger<PluginCatalog> _logger;
    private readonly string _pluginRoot;
    private readonly object _lock = new();
    private List<PluginRegistration> _registrations = [];
    private bool _loaded;

    public PluginCatalog(
        IEnumerable<ITuvimaPlugin> builtInPlugins,
        PluginSettingsStore settingsStore,
        IConfigurationLoader configurationLoader,
        ILogger<PluginCatalog> logger)
    {
        _builtInPlugins = builtInPlugins.ToList();
        _settingsStore = settingsStore;
        _logger = logger;

        var core = configurationLoader.LoadCore();
        var root = string.IsNullOrWhiteSpace(core.LibraryRoot)
            ? Path.GetFullPath(".data")
            : Path.Combine(core.LibraryRoot, ".data");
        _pluginRoot = Path.Combine(root, "plugins");
    }

    public IReadOnlyList<PluginRegistration> List()
    {
        EnsureLoaded();
        lock (_lock) return _registrations.ToList();
    }

    public PluginRegistration? Get(string pluginId)
    {
        EnsureLoaded();
        lock (_lock)
        {
            return _registrations.FirstOrDefault(p => string.Equals(p.Manifest.Id, pluginId, StringComparison.OrdinalIgnoreCase));
        }
    }

    public void SetEnabled(string pluginId, bool enabled)
    {
        var registration = Get(pluginId)
            ?? throw new InvalidOperationException($"Plugin '{pluginId}' is not registered.");
        _settingsStore.SetEnabled(registration.Manifest, enabled);
        Reload();
    }

    public void SaveSettings(string pluginId, Dictionary<string, JsonElement> settings)
    {
        var registration = Get(pluginId)
            ?? throw new InvalidOperationException($"Plugin '{pluginId}' is not registered.");
        ValidateSettings(registration, settings);
        var config = _settingsStore.Load(registration.Manifest);
        config.Settings = settings;
        _settingsStore.Save(config);
        Reload();
    }

    public string GetManifestJson(string pluginId)
    {
        var registration = Get(pluginId)
            ?? throw new InvalidOperationException($"Plugin '{pluginId}' is not registered.");

        if (registration.IsBuiltIn || string.IsNullOrWhiteSpace(registration.ManifestPath))
            throw new InvalidOperationException("Built-in plugin manifests are compiled into the application.");

        return File.ReadAllText(registration.ManifestPath);
    }

    public void SaveManifestJson(string pluginId, string json)
    {
        var registration = Get(pluginId)
            ?? throw new InvalidOperationException($"Plugin '{pluginId}' is not registered.");

        if (registration.IsBuiltIn || string.IsNullOrWhiteSpace(registration.ManifestPath))
            throw new InvalidOperationException("Built-in plugin manifests are compiled into the application.");

        using var document = JsonDocument.Parse(json, JsonDocumentOptions);
        var manifest = document.Deserialize<PluginManifest>(JsonOptions)
            ?? throw new InvalidOperationException("Plugin manifest JSON is empty.");

        if (string.IsNullOrWhiteSpace(manifest.Id))
            throw new InvalidOperationException("Plugin manifest must include an id.");

        if (!string.Equals(manifest.Id, registration.Manifest.Id, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Changing a plugin id from the JSON editor is not supported.");

        if (string.IsNullOrWhiteSpace(manifest.EntryAssembly) || string.IsNullOrWhiteSpace(manifest.EntryType))
            throw new InvalidOperationException("Plugin manifest must include entry_assembly and entry_type.");

        File.WriteAllText(registration.ManifestPath, JsonSerializer.Serialize(manifest, WriteJsonOptions));
        Reload();
    }

    public void DeletePlugin(string pluginId)
    {
        var registration = Get(pluginId)
            ?? throw new InvalidOperationException($"Plugin '{pluginId}' is not registered.");

        if (registration.IsBuiltIn || string.IsNullOrWhiteSpace(registration.ManifestPath))
            throw new InvalidOperationException("Built-in plugins cannot be deleted.");

        var pluginDirectory = Path.GetFullPath(Path.GetDirectoryName(registration.ManifestPath)!);
        var pluginRoot = Path.GetFullPath(_pluginRoot);
        var normalizedRoot = pluginRoot.EndsWith(Path.DirectorySeparatorChar)
            ? pluginRoot
            : pluginRoot + Path.DirectorySeparatorChar;

        if (!pluginDirectory.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Plugin directory is outside the configured plugin root.");

        Directory.Delete(pluginDirectory, recursive: true);
        _settingsStore.Delete(registration.Manifest.Id);
        Reload();
    }

    public IReadOnlyList<IPlaybackSegmentDetector> GetEnabledSegmentDetectors()
    {
        EnsureLoaded();
        lock (_lock)
        {
            return _registrations
                .Where(r => r.Enabled && r.LoadError is null)
                .SelectMany(r => r.Capabilities.OfType<IPlaybackSegmentDetector>())
                .ToList();
        }
    }

    public void Reload()
    {
        lock (_lock)
        {
            _loaded = false;
            _registrations = [];
        }
        EnsureLoaded();
    }

    private void EnsureLoaded()
    {
        lock (_lock)
        {
            if (_loaded) return;

            var registrations = new List<PluginRegistration>();
            foreach (var plugin in _builtInPlugins)
                registrations.Add(BuildRegistration(plugin, plugin.Manifest, isBuiltIn: true, loadError: null, manifestPath: null));

            registrations.AddRange(LoadFilePlugins());
            _registrations = registrations
                .GroupBy(r => r.Manifest.Id, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderBy(r => r.Manifest.Name)
                .ToList();
            _loaded = true;
        }
    }

    private PluginRegistration BuildRegistration(
        ITuvimaPlugin plugin,
        PluginManifest manifest,
        bool isBuiltIn,
        string? loadError,
        string? manifestPath)
    {
        var config = _settingsStore.Load(manifest);
        IReadOnlyList<IPluginCapability> capabilities = [];
        if (loadError is null)
        {
            try { capabilities = plugin.CreateCapabilities(); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Plugin {PluginId} failed to create capabilities", manifest.Id);
                loadError = ex.Message;
            }
        }

        var settingsSchema = ResolveSettingsSchema(manifest, capabilities);
        return new PluginRegistration(
            manifest,
            config.Enabled,
            isBuiltIn,
            capabilities,
            config.Settings,
            loadError,
            settingsSchema,
            manifestPath);
    }

    private JsonElement? ResolveSettingsSchema(
        PluginManifest manifest,
        IReadOnlyList<IPluginCapability> capabilities)
    {
        if (manifest.SettingsSchema is { } manifestSchema && IsConcreteJson(manifestSchema))
            return manifestSchema.Clone();

        foreach (var provider in capabilities.OfType<IPluginSettingsSchemaProvider>())
        {
            try
            {
                var schema = provider.GetSettingsSchema();
                if (IsConcreteJson(schema))
                    return schema.Clone();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Plugin {PluginId} failed to provide a settings schema", manifest.Id);
            }
        }

        return null;
    }

    private static bool IsConcreteJson(JsonElement element) =>
        element.ValueKind is not JsonValueKind.Undefined and not JsonValueKind.Null;

    private static void ValidateSettings(PluginRegistration registration, IReadOnlyDictionary<string, JsonElement> settings)
    {
        if (registration.SettingsSchema is not { } schema || schema.ValueKind != JsonValueKind.Object)
            return;

        var properties = TryGetPropertyObject(schema, "properties");
        if (properties is null)
            return;

        var required = ReadStringArray(schema, "required");
        foreach (var key in required)
        {
            if (!settings.ContainsKey(key))
                throw new InvalidOperationException($"Plugin setting '{key}' is required.");
        }

        foreach (var (key, value) in settings)
        {
            if (!properties.Value.TryGetProperty(key, out var definition) || definition.ValueKind != JsonValueKind.Object)
                continue;

            ValidateSettingValue(key, value, definition);
        }
    }

    private static void ValidateSettingValue(string key, JsonElement value, JsonElement definition)
    {
        var expectedType = ReadString(definition, "type");
        if (!string.IsNullOrWhiteSpace(expectedType) && !SettingTypeMatches(expectedType, value))
            throw new InvalidOperationException($"Plugin setting '{key}' must be {expectedType}.");

        var allowedValues = ReadEnumValues(definition);
        if (allowedValues.Count > 0 && !allowedValues.Contains(ScalarSettingValue(value), StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Plugin setting '{key}' is not one of the allowed values.");

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var numeric))
        {
            if (TryReadDouble(definition, "minimum", out var min) && numeric < min)
                throw new InvalidOperationException($"Plugin setting '{key}' must be at least {min}.");
            if (TryReadDouble(definition, "maximum", out var max) && numeric > max)
                throw new InvalidOperationException($"Plugin setting '{key}' must be at most {max}.");
        }
    }

    private static JsonElement? TryGetPropertyObject(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.Object
            ? property
            : null;

    private static string? ReadString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static IReadOnlyList<string> ReadStringArray(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Array)
            return [];

        return property.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .ToList();
    }

    private static IReadOnlyList<string> ReadEnumValues(JsonElement definition)
    {
        if (!definition.TryGetProperty("enum", out var property) || property.ValueKind != JsonValueKind.Array)
            return [];

        return property.EnumerateArray()
            .Select(ScalarSettingValue)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList();
    }

    private static bool TryReadDouble(JsonElement element, string propertyName, out double value)
    {
        if (element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.Number)
            return property.TryGetDouble(out value);

        value = default;
        return false;
    }

    private static bool SettingTypeMatches(string expectedType, JsonElement value)
    {
        return expectedType.ToLowerInvariant() switch
        {
            "boolean" or "bool" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "integer" or "int" => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _),
            "number" => value.ValueKind == JsonValueKind.Number,
            "string" => value.ValueKind == JsonValueKind.String,
            "array" => value.ValueKind == JsonValueKind.Array,
            "object" => value.ValueKind == JsonValueKind.Object,
            _ => true,
        };
    }

    private static string ScalarSettingValue(JsonElement value) =>
        value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => value.GetRawText(),
        };

    private IEnumerable<PluginRegistration> LoadFilePlugins()
    {
        var results = new List<PluginRegistration>();
        if (!Directory.Exists(_pluginRoot)) return results;

        foreach (var manifestPath in Directory.EnumerateFiles(_pluginRoot, "plugin.json", SearchOption.AllDirectories))
        {
            PluginManifest? manifest = null;
            try
            {
                manifest = JsonSerializer.Deserialize<PluginManifest>(File.ReadAllText(manifestPath), JsonOptions);
                if (manifest is null || string.IsNullOrWhiteSpace(manifest.EntryAssembly) || string.IsNullOrWhiteSpace(manifest.EntryType))
                {
                    results.Add(new PluginRegistration(
                        manifest ?? new PluginManifest { Id = Path.GetFileName(Path.GetDirectoryName(manifestPath)) ?? "unknown", Name = "Invalid plugin" },
                        false,
                        false,
                        [],
                        new Dictionary<string, JsonElement>(),
                        "Plugin manifest must include entry_assembly and entry_type.",
                        null,
                        manifestPath));
                    continue;
                }

                var assemblyPath = Path.Combine(Path.GetDirectoryName(manifestPath)!, manifest.EntryAssembly);
                if (!File.Exists(assemblyPath))
                {
                    results.Add(new PluginRegistration(manifest, false, false, [], new Dictionary<string, JsonElement>(), "Entry assembly was not found.", null, manifestPath));
                    continue;
                }

                var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(assemblyPath));
                var type = assembly.GetType(manifest.EntryType, throwOnError: false);
                if (type is null || !typeof(ITuvimaPlugin).IsAssignableFrom(type))
                {
                    results.Add(new PluginRegistration(manifest, false, false, [], new Dictionary<string, JsonElement>(), "Entry type does not implement ITuvimaPlugin.", null, manifestPath));
                    continue;
                }

                var plugin = (ITuvimaPlugin?)Activator.CreateInstance(type);
                results.Add(plugin is null
                    ? new PluginRegistration(manifest, false, false, [], new Dictionary<string, JsonElement>(), "Entry type could not be constructed.", null, manifestPath)
                    : BuildRegistration(plugin, manifest, isBuiltIn: false, loadError: null, manifestPath: manifestPath));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load plugin manifest {ManifestPath}", manifestPath);
                results.Add(new PluginRegistration(
                    manifest ?? new PluginManifest { Id = Path.GetFileName(Path.GetDirectoryName(manifestPath)) ?? "unknown", Name = "Invalid plugin" },
                    false,
                    false,
                    [],
                    new Dictionary<string, JsonElement>(),
                    ex.Message,
                    null,
                    manifestPath));
            }
        }

        return results;
    }
}

public sealed record PluginRegistration(
    PluginManifest Manifest,
    bool Enabled,
    bool IsBuiltIn,
    IReadOnlyList<IPluginCapability> Capabilities,
    IReadOnlyDictionary<string, JsonElement> Settings,
    string? LoadError,
    JsonElement? SettingsSchema,
    string? ManifestPath);

