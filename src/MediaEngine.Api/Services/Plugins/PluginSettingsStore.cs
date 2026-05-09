using System.Text.Json;
using MediaEngine.Plugins;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Api.Services.Plugins;

public sealed class PluginSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    private readonly string _configRoot;

    public PluginSettingsStore(IConfigurationLoader configurationLoader)
    {
        var core = configurationLoader.LoadCore();
        var root = string.IsNullOrWhiteSpace(core.LibraryRoot)
            ? Path.GetFullPath(".data")
            : Path.Combine(core.LibraryRoot, ".data");
        _configRoot = Path.Combine(root, "plugin-config");
        Directory.CreateDirectory(_configRoot);
    }

    public PluginUserConfiguration Load(PluginManifest manifest)
    {
        var path = GetPath(manifest.Id);
        if (!File.Exists(path))
        {
            return new PluginUserConfiguration
            {
                PluginId = manifest.Id,
                Enabled = false,
                Settings = new Dictionary<string, JsonElement>(manifest.DefaultSettings, StringComparer.OrdinalIgnoreCase),
            };
        }

        try
        {
            var config = JsonSerializer.Deserialize<PluginUserConfiguration>(File.ReadAllText(path), JsonOptions);
            if (config is null) return new PluginUserConfiguration { PluginId = manifest.Id };
            config.Settings ??= new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            foreach (var (key, value) in manifest.DefaultSettings)
                config.Settings.TryAdd(key, value);
            return config;
        }
        catch
        {
            return new PluginUserConfiguration
            {
                PluginId = manifest.Id,
                Enabled = false,
                Settings = new Dictionary<string, JsonElement>(manifest.DefaultSettings, StringComparer.OrdinalIgnoreCase),
            };
        }
    }

    public void Save(PluginUserConfiguration config)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(config.PluginId);
        Directory.CreateDirectory(_configRoot);
        File.WriteAllText(GetPath(config.PluginId), JsonSerializer.Serialize(config, JsonOptions));
    }

    public void SetEnabled(PluginManifest manifest, bool enabled)
    {
        var config = Load(manifest);
        config.Enabled = enabled;
        Save(config);
    }

    public void Delete(string pluginId)
    {
        var path = GetPath(pluginId);
        if (File.Exists(path))
            File.Delete(path);
    }

    private string GetPath(string pluginId)
    {
        var safe = string.Concat(pluginId.Select(ch => char.IsLetterOrDigit(ch) || ch is '.' or '-' or '_' ? ch : '_'));
        return Path.Combine(_configRoot, $"{safe}.json");
    }
}

public sealed class PluginUserConfiguration
{
    public string PluginId { get; set; } = "";
    public bool Enabled { get; set; }
    public Dictionary<string, JsonElement> Settings { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
