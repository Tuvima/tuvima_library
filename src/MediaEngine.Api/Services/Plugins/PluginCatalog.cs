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

        return new PluginRegistration(
            manifest,
            config.Enabled,
            isBuiltIn,
            capabilities,
            config.Settings,
            loadError,
            manifestPath);
    }

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
                        manifestPath));
                    continue;
                }

                var assemblyPath = Path.Combine(Path.GetDirectoryName(manifestPath)!, manifest.EntryAssembly);
                if (!File.Exists(assemblyPath))
                {
                    results.Add(new PluginRegistration(manifest, false, false, [], new Dictionary<string, JsonElement>(), "Entry assembly was not found.", manifestPath));
                    continue;
                }

                var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(assemblyPath));
                var type = assembly.GetType(manifest.EntryType, throwOnError: false);
                if (type is null || !typeof(ITuvimaPlugin).IsAssignableFrom(type))
                {
                    results.Add(new PluginRegistration(manifest, false, false, [], new Dictionary<string, JsonElement>(), "Entry type does not implement ITuvimaPlugin.", manifestPath));
                    continue;
                }

                var plugin = (ITuvimaPlugin?)Activator.CreateInstance(type);
                results.Add(plugin is null
                    ? new PluginRegistration(manifest, false, false, [], new Dictionary<string, JsonElement>(), "Entry type could not be constructed.", manifestPath)
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
    string? ManifestPath);

