using System.Text.Json;
using Tanaste.Storage.Contracts;
using Tanaste.Storage.Models;

namespace Tanaste.Storage;

/// <summary>
/// Multi-file configuration loader that reads and writes individual JSON files
/// from a structured directory layout.
///
/// <para>
/// <b>Directory layout:</b>
/// <code>
/// config/
///   tanaste.json              ← CoreConfiguration
///   scoring.json              ← ScoringSettings
///   maintenance.json          ← MaintenanceSettings
///   providers/                ← Per-provider ProviderConfiguration
///     local_filesystem.json
///     apple_books_ebook.json
///     wikidata.json
///     …
///   universe/                 ← Universe knowledge models
///     wikidata.json
///     …
/// </code>
/// </para>
///
/// <para>
/// <b>Initialization order:</b>
/// <list type="number">
/// <item>If the config directory already exists, it is used as-is.</item>
/// <item>If the config directory does not exist but a legacy
///       <c>tanaste_master.json</c> is found, the legacy file is
///       automatically split into the directory structure and renamed
///       to <c>.migrated</c>.</item>
/// <item>If neither exists (first run), a default directory is created
///       with sensible defaults for all configuration files.</item>
/// </list>
/// </para>
///
/// <para>
/// Implements both <see cref="IConfigurationLoader"/> (granular access)
/// and <see cref="IStorageManifest"/> (backward compatibility). The
/// <see cref="IStorageManifest.Load"/> method assembles all individual
/// files into a composite <see cref="TanasteMasterManifest"/>.
/// </para>
/// </summary>
public sealed class ConfigurationDirectoryLoader : IConfigurationLoader, IStorageManifest
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented       = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    private readonly string  _configDir;
    private readonly string? _legacyManifestPath;

    // ── Subdirectory constants ────────────────────────────────────────────────

    private const string ProvidersSubdir = "providers";
    private const string UniverseSubdir  = "universe";

    // ── File names ────────────────────────────────────────────────────────────

    private const string CoreFileName        = "tanaste.json";
    private const string ScoringFileName     = "scoring.json";
    private const string MaintenanceFileName = "maintenance.json";

    // ── Endpoint distribution map for legacy migration ────────────────────────

    /// <summary>
    /// Maps legacy <c>provider_endpoints</c> keys to the provider names that
    /// should receive them during migration from <c>tanaste_master.json</c>.
    /// </summary>
    private static readonly Dictionary<string, string[]> EndpointToProviders = new(StringComparer.OrdinalIgnoreCase)
    {
        ["apple_books"]     = ["apple_books_ebook", "apple_books_audiobook"],
        ["audnexus"]        = ["audnexus"],
        ["wikidata_api"]    = ["wikidata"],
        ["wikidata_sparql"] = ["wikidata"],
    };

    // ── Constructor ───────────────────────────────────────────────────────────

    /// <param name="configDirectoryPath">
    /// Root directory for all configuration files (e.g. <c>"config"</c>).
    /// </param>
    /// <param name="legacyManifestPath">
    /// Optional path to the legacy <c>tanaste_master.json</c> file.
    /// If the config directory does not exist and the legacy file is found,
    /// an automatic migration is performed.
    /// </param>
    public ConfigurationDirectoryLoader(string configDirectoryPath, string? legacyManifestPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configDirectoryPath);
        _configDir          = configDirectoryPath;
        _legacyManifestPath = legacyManifestPath;

        EnsureInitialized();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  IStorageManifest — Backward Compatibility
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Assembles all individual configuration files into a composite
    /// <see cref="TanasteMasterManifest"/>. Existing consumers that depend
    /// on the monolithic manifest shape continue to work unchanged.
    /// </summary>
    public TanasteMasterManifest Load()
    {
        var core        = LoadCore();
        var scoring     = LoadScoring();
        var maintenance = LoadMaintenance();
        var providers   = LoadAllProviders();

        // Assemble provider bootstraps + composite endpoint map
        var bootstraps = new List<ProviderBootstrap>(providers.Count);
        var endpoints  = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var p in providers)
        {
            bootstraps.Add(ToBootstrap(p));

            // Merge all provider endpoints into the composite map.
            // Keys are already in the composite format (e.g. "wikidata_sparql")
            // because the individual files store them that way.
            foreach (var (key, url) in p.Endpoints)
            {
                endpoints.TryAdd(key, url);
            }
        }

        return new TanasteMasterManifest
        {
            SchemaVersion          = core.SchemaVersion,
            DatabasePath           = core.DatabasePath,
            DataRoot               = core.DataRoot,
            WatchDirectory         = core.WatchDirectory,
            LibraryRoot            = core.LibraryRoot,
            OrganizationTemplate   = core.OrganizationTemplate,
            Providers              = bootstraps,
            Scoring                = scoring,
            Maintenance            = maintenance,
            ProviderEndpoints      = endpoints,
            // WikidataPropertyMap overrides are no longer stored here;
            // they live in the universe config. Return empty for compat.
            WikidataPropertyMap    = [],
        };
    }

    /// <summary>
    /// Distributes a composite <see cref="TanasteMasterManifest"/> back into
    /// individual configuration files. Used by legacy consumers that still call
    /// <see cref="IStorageManifest.Save"/>.
    /// </summary>
    public void Save(TanasteMasterManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        SaveCore(new CoreConfiguration
        {
            SchemaVersion        = manifest.SchemaVersion,
            DatabasePath         = manifest.DatabasePath,
            DataRoot             = manifest.DataRoot,
            WatchDirectory       = manifest.WatchDirectory,
            LibraryRoot          = manifest.LibraryRoot,
            OrganizationTemplate = manifest.OrganizationTemplate,
        });

        SaveScoring(manifest.Scoring);
        SaveMaintenance(manifest.Maintenance);

        // Distribute endpoints to matching provider configs.
        var endpointsByProvider = DistributeEndpoints(manifest.ProviderEndpoints);

        foreach (var bootstrap in manifest.Providers)
        {
            var providerConfig = FromBootstrap(bootstrap);

            // Merge distributed endpoints into this provider's config.
            if (endpointsByProvider.TryGetValue(bootstrap.Name, out var provEndpoints))
            {
                foreach (var (key, url) in provEndpoints)
                    providerConfig.Endpoints.TryAdd(key, url);
            }

            SaveProvider(providerConfig);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  IConfigurationLoader — Granular Access
    // ═══════════════════════════════════════════════════════════════════════════

    /// <inheritdoc/>
    public CoreConfiguration LoadCore() =>
        LoadFile<CoreConfiguration>(CoreFileName) ?? new();

    /// <inheritdoc/>
    public void SaveCore(CoreConfiguration config) =>
        SaveFile(CoreFileName, config);

    /// <inheritdoc/>
    public ScoringSettings LoadScoring() =>
        LoadFile<ScoringSettings>(ScoringFileName) ?? new();

    /// <inheritdoc/>
    public void SaveScoring(ScoringSettings settings) =>
        SaveFile(ScoringFileName, settings);

    /// <inheritdoc/>
    public MaintenanceSettings LoadMaintenance() =>
        LoadFile<MaintenanceSettings>(MaintenanceFileName) ?? new();

    /// <inheritdoc/>
    public void SaveMaintenance(MaintenanceSettings settings) =>
        SaveFile(MaintenanceFileName, settings);

    /// <inheritdoc/>
    public ProviderConfiguration? LoadProvider(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return LoadFile<ProviderConfiguration>(Path.Combine(ProvidersSubdir, $"{name}.json"));
    }

    /// <inheritdoc/>
    public void SaveProvider(ProviderConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentException.ThrowIfNullOrWhiteSpace(config.Name);
        SaveFile(Path.Combine(ProvidersSubdir, $"{config.Name}.json"), config);
    }

    /// <inheritdoc/>
    public IReadOnlyList<ProviderConfiguration> LoadAllProviders()
    {
        var dir = Path.Combine(_configDir, ProvidersSubdir);
        if (!Directory.Exists(dir))
            return [];

        var results = new List<ProviderConfiguration>();
        foreach (var file in Directory.EnumerateFiles(dir, "*.json"))
        {
            var config = TryDeserializeFile<ProviderConfiguration>(file);
            if (config is not null)
                results.Add(config);
        }
        return results;
    }

    /// <inheritdoc/>
    public T? LoadConfig<T>(string subdirectory, string name) where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subdirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return LoadFile<T>(Path.Combine(subdirectory, $"{name}.json"));
    }

    /// <inheritdoc/>
    public void SaveConfig<T>(string subdirectory, string name, T config) where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subdirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(config);
        SaveFile(Path.Combine(subdirectory, $"{name}.json"), config);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  File I/O — Generic Read / Write with .bak Rotation
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Reads and deserializes a JSON file relative to the config directory.
    /// Falls back to <c>.bak</c> if the primary file is missing or corrupt.
    /// Returns <c>null</c> if both attempts fail.
    /// </summary>
    private T? LoadFile<T>(string relativePath) where T : class
    {
        var fullPath   = Path.Combine(_configDir, relativePath);
        var backupPath = fullPath + ".bak";

        // Attempt 1 — primary file
        var result = TryDeserializeFile<T>(fullPath);
        if (result is not null)
            return result;

        // Attempt 2 — backup file; restore primary on success
        result = TryDeserializeFile<T>(backupPath);
        if (result is not null)
        {
            try { File.Copy(backupPath, fullPath, overwrite: true); }
            catch { /* Best-effort restore */ }
            return result;
        }

        return null;
    }

    /// <summary>
    /// Serializes and writes a JSON file relative to the config directory.
    /// Rotates the existing file to <c>.bak</c> before overwriting.
    /// Creates parent directories as needed.
    /// </summary>
    private void SaveFile<T>(string relativePath, T config) where T : class
    {
        var fullPath   = Path.Combine(_configDir, relativePath);
        var backupPath = fullPath + ".bak";

        // Ensure parent directory exists
        var dir = Path.GetDirectoryName(fullPath);
        if (dir is not null && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        // Rotate current to backup
        if (File.Exists(fullPath))
        {
            try { File.Copy(fullPath, backupPath, overwrite: true); }
            catch { /* Best-effort backup */ }
        }

        var json = JsonSerializer.Serialize(config, JsonOptions);
        File.WriteAllText(fullPath, json);
    }

    /// <summary>Deserialize a file; returns <c>null</c> on any failure.</summary>
    private static T? TryDeserializeFile<T>(string path) where T : class
    {
        if (!File.Exists(path))
            return null;

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch (JsonException) { return null; }
        catch (IOException)   { return null; }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  Initialization — Migration & First-Run
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Ensures the config directory exists and is populated.
    /// Called once from the constructor.
    /// </summary>
    private void EnsureInitialized()
    {
        if (Directory.Exists(_configDir))
            return; // Already initialized

        // Try legacy migration
        if (_legacyManifestPath is not null && File.Exists(_legacyManifestPath))
        {
            MigrateFromLegacy(_legacyManifestPath);
            return;
        }

        // First run — create defaults
        CreateDefaultDirectory();
    }

    /// <summary>
    /// Splits a legacy <c>tanaste_master.json</c> into the multi-file
    /// directory structure. Renames the legacy file to <c>.migrated</c>
    /// after successful migration.
    /// </summary>
    private void MigrateFromLegacy(string legacyPath)
    {
        TanasteMasterManifest? manifest = null;
        try
        {
            var json = File.ReadAllText(legacyPath);
            manifest = JsonSerializer.Deserialize<TanasteMasterManifest>(json, JsonOptions);
        }
        catch { /* Fall through to create defaults */ }

        if (manifest is null)
        {
            CreateDefaultDirectory();
            return;
        }

        // Create the directory structure
        Directory.CreateDirectory(_configDir);
        Directory.CreateDirectory(Path.Combine(_configDir, ProvidersSubdir));
        Directory.CreateDirectory(Path.Combine(_configDir, UniverseSubdir));

        // Write core config
        SaveCore(new CoreConfiguration
        {
            SchemaVersion        = manifest.SchemaVersion,
            DatabasePath         = manifest.DatabasePath,
            DataRoot             = manifest.DataRoot,
            WatchDirectory       = manifest.WatchDirectory,
            LibraryRoot          = manifest.LibraryRoot,
            OrganizationTemplate = manifest.OrganizationTemplate,
        });

        // Write scoring and maintenance as-is
        SaveScoring(manifest.Scoring);
        SaveMaintenance(manifest.Maintenance);

        // Distribute endpoints to providers
        var endpointsByProvider = DistributeEndpoints(manifest.ProviderEndpoints);

        // Write individual provider configs
        foreach (var bootstrap in manifest.Providers)
        {
            var providerConfig = FromBootstrap(bootstrap);

            // Apply rate limit defaults based on known provider behaviour
            providerConfig.ThrottleMs     = GetDefaultThrottleMs(bootstrap.Name);
            providerConfig.MaxConcurrency = GetDefaultMaxConcurrency(bootstrap.Name);

            // Merge distributed endpoints
            if (endpointsByProvider.TryGetValue(bootstrap.Name, out var provEndpoints))
            {
                foreach (var (key, url) in provEndpoints)
                    providerConfig.Endpoints.TryAdd(key, url);
            }

            SaveProvider(providerConfig);
        }

        // Rename legacy file to .migrated
        try
        {
            var migratedPath = legacyPath + ".migrated";
            if (File.Exists(migratedPath))
                File.Delete(migratedPath);
            File.Move(legacyPath, migratedPath);
        }
        catch { /* Best-effort rename */ }
    }

    /// <summary>
    /// Creates a default config directory with sensible defaults when no
    /// prior configuration exists (first-run experience).
    /// </summary>
    private void CreateDefaultDirectory()
    {
        Directory.CreateDirectory(_configDir);
        Directory.CreateDirectory(Path.Combine(_configDir, ProvidersSubdir));
        Directory.CreateDirectory(Path.Combine(_configDir, UniverseSubdir));
        Directory.CreateDirectory(Path.Combine(_configDir, "ui"));
        Directory.CreateDirectory(Path.Combine(_configDir, "ui", "devices"));
        Directory.CreateDirectory(Path.Combine(_configDir, "ui", "profiles"));

        // Core defaults
        SaveCore(new CoreConfiguration());
        SaveScoring(new ScoringSettings());
        SaveMaintenance(new MaintenanceSettings());

        // Default providers — same set as the legacy CreateDefaultManifest()
        SaveProvider(new ProviderConfiguration
        {
            Name    = "local_filesystem",
            Enabled = true,
            Weight  = 1.0,
            Domain  = ProviderDomain.Universal,
        });

        SaveProvider(new ProviderConfiguration
        {
            Name           = "apple_books_ebook",
            Enabled        = true,
            Weight         = 0.7,
            Domain         = ProviderDomain.Ebook,
            CapabilityTags = ["cover", "description", "rating"],
            FieldWeights   = new() { ["cover"] = 0.9, ["description"] = 0.9, ["rating"] = 0.8 },
            Endpoints      = new() { ["apple_books"] = "https://itunes.apple.com" },
            ThrottleMs     = 300,
        });

        SaveProvider(new ProviderConfiguration
        {
            Name           = "apple_books_audiobook",
            Enabled        = true,
            Weight         = 0.7,
            Domain         = ProviderDomain.Audiobook,
            CapabilityTags = ["cover"],
            FieldWeights   = new() { ["cover"] = 0.6 },
            Endpoints      = new() { ["apple_books"] = "https://itunes.apple.com" },
            ThrottleMs     = 300,
        });

        SaveProvider(new ProviderConfiguration
        {
            Name           = "audnexus",
            Enabled        = true,
            Weight         = 0.7,
            Domain         = ProviderDomain.Audiobook,
            CapabilityTags = ["cover", "narrator", "series"],
            FieldWeights   = new() { ["cover"] = 0.9, ["narrator"] = 0.9, ["series"] = 0.9 },
            Endpoints      = new() { ["audnexus"] = "https://api.audnexus.com" },
        });

        SaveProvider(new ProviderConfiguration
        {
            Name           = "open_library",
            Enabled        = false,
            Weight         = 0.7,
            Domain         = ProviderDomain.Ebook,
            CapabilityTags = ["series"],
            FieldWeights   = new() { ["series"] = 0.9 },
        });

        SaveProvider(new ProviderConfiguration
        {
            Name           = "wikidata",
            Enabled        = true,
            Weight         = 0.7,
            Domain         = ProviderDomain.Universal,
            CapabilityTags = ["series", "franchise", "person_id"],
            FieldWeights   = new() { ["series"] = 1.0, ["franchise"] = 1.0, ["person_id"] = 1.0 },
            Endpoints      = new()
            {
                ["wikidata_api"]   = "https://www.wikidata.org/w/api.php",
                ["wikidata_sparql"] = "https://query.wikidata.org/sparql",
            },
            ThrottleMs     = 1100,
        });

        // Default UI configuration
        SaveConfig("ui", "global", new UIGlobalSettings());
        SaveConfig("ui/devices", "web", new UIDeviceProfile
        {
            DeviceClass = "web",
            DisplayName = "Desktop Web",
        });
        SaveConfig("ui/devices", "mobile", new UIDeviceProfile
        {
            DeviceClass     = "mobile",
            DisplayName     = "Mobile",
            ContentPadding  = "pa-2",
            ContentMaxWidth = "Full",
            Constraints     = new UIDeviceConstraints
            {
                FeaturesDisabled = ["view_toggle"],
                MinTouchTargetPx = 48,
            },
            Shell = new UIShellSettings
            {
                AppBarStyle     = "compact",
                LogoVariant     = "icon",
                IntentDockItems = ["Hubs", "Watch", "Read", "Listen"],
                IntentDockStyle = "normal",
            },
            Pages = new UIPageSettings
            {
                Home = new UIHomePageSettings
                {
                    HubHeroLayout       = "stacked",
                    ProgressCardsLayout = "stacked",
                    BentoColumns        = 1,
                    PendingFilesDisplay = "badge",
                },
                Preferences = new UIPreferencesPageSettings
                {
                    TabBarLayout     = "vertical",
                    GeneralTabLayout = "stacked",
                    ColorSwatchCount = 4,
                },
                ServerSettings = new UIServerSettingsPageSettings
                {
                    TabBarLayout     = "vertical-accordion",
                    TabContentLayout = "stacked-card",
                },
            },
        });
        SaveConfig("ui/devices", "television", new UIDeviceProfile
        {
            DeviceClass     = "television",
            DisplayName     = "Television",
            ContentPadding  = "pa-6",
            ContentMaxWidth = "Full",
            BorderRadius    = 40,
            Constraints     = new UIDeviceConstraints
            {
                FeaturesDisabled = [
                    "command_palette", "search_button", "theme_toggle",
                    "avatar_menu", "pending_files_alert", "view_toggle",
                    "profile_section", "color_picker",
                ],
                PagesDisabled  = ["server_settings"],
                AllowTextInput = false,
                MinTouchTargetPx = 64,
            },
            Shell = new UIShellSettings
            {
                AppBarStyle     = "oversized",
                LogoVariant     = "wordmark-large",
                IntentDockItems = ["Hubs", "Watch", "Read", "Listen"],
                IntentDockStyle = "oversized",
            },
            Pages = new UIPageSettings
            {
                Home = new UIHomePageSettings
                {
                    HubHeroLayout       = "two-column-oversized",
                    ProgressCardsLayout = "row-oversized",
                    BentoColumns        = 2,
                    BentoTileStyle      = "large",
                    PendingFilesDisplay = "hidden",
                },
                Preferences = new UIPreferencesPageSettings
                {
                    TabBarLayout     = "focus-nav",
                    GeneralTabLayout = "theme-only",
                    ColorSwatchCount = 0,
                },
                ServerSettings = new UIServerSettingsPageSettings { PageEnabled = false },
            },
        });
        SaveConfig("ui/devices", "automotive", new UIDeviceProfile
        {
            DeviceClass     = "automotive",
            DisplayName     = "Automotive",
            DarkMode        = true,
            ContentMaxWidth = "Full",
            Constraints     = new UIDeviceConstraints
            {
                FeaturesDisabled = [
                    "command_palette", "search_button", "theme_toggle",
                    "avatar_menu", "pending_files_alert", "view_toggle",
                    "profile_section", "color_picker", "server_settings",
                ],
                PagesDisabled    = ["server_settings"],
                AllowTextInput   = false,
                MinTouchTargetPx = 80,
                ForceDarkMode    = true,
            },
            Shell = new UIShellSettings
            {
                AppBarStyle     = "minimal",
                LogoVariant     = "icon-large",
                IntentDockItems = ["Hubs", "Listen"],
                IntentDockStyle = "oversized",
            },
            Pages = new UIPageSettings
            {
                Home = new UIHomePageSettings
                {
                    HubHeroEnabled      = false,
                    HubHeroLayout       = "hidden",
                    ProgressCardsLayout = "single",
                    BentoColumns        = 1,
                    BentoTileStyle      = "audio-only",
                    PendingFilesDisplay = "hidden",
                },
                Preferences = new UIPreferencesPageSettings
                {
                    TabBarLayout     = "single",
                    GeneralTabLayout = "theme-only",
                    ColorSwatchCount = 0,
                },
                ServerSettings = new UIServerSettingsPageSettings { PageEnabled = false },
            },
        });
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  Conversion Helpers — ProviderBootstrap ↔ ProviderConfiguration
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>Convert a <see cref="ProviderConfiguration"/> to a legacy <see cref="ProviderBootstrap"/>.</summary>
    private static ProviderBootstrap ToBootstrap(ProviderConfiguration config) => new()
    {
        Name           = config.Name,
        Version        = config.Version,
        Enabled        = config.Enabled,
        Weight         = config.Weight,
        Domain         = config.Domain,
        CapabilityTags = [.. config.CapabilityTags],
        FieldWeights   = new(config.FieldWeights),
    };

    /// <summary>Convert a legacy <see cref="ProviderBootstrap"/> to a <see cref="ProviderConfiguration"/>.</summary>
    private static ProviderConfiguration FromBootstrap(ProviderBootstrap bootstrap) => new()
    {
        Name           = bootstrap.Name,
        Version        = bootstrap.Version,
        Enabled        = bootstrap.Enabled,
        Weight         = bootstrap.Weight,
        Domain         = bootstrap.Domain,
        CapabilityTags = [.. bootstrap.CapabilityTags],
        FieldWeights   = new(bootstrap.FieldWeights),
    };

    // ═══════════════════════════════════════════════════════════════════════════
    //  Endpoint Distribution
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Distributes a composite <c>provider_endpoints</c> dictionary into
    /// per-provider endpoint dictionaries using the known mapping.
    /// </summary>
    private static Dictionary<string, Dictionary<string, string>> DistributeEndpoints(
        Dictionary<string, string> compositeEndpoints)
    {
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var (endpointKey, url) in compositeEndpoints)
        {
            if (EndpointToProviders.TryGetValue(endpointKey, out var providerNames))
            {
                foreach (var providerName in providerNames)
                {
                    if (!result.TryGetValue(providerName, out var dict))
                    {
                        dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        result[providerName] = dict;
                    }
                    dict.TryAdd(endpointKey, url);
                }
            }
        }

        return result;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  Default Rate Limits — Known Provider Behaviour
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Returns the default throttle delay for a known provider name.
    /// Previously hard-coded in adapter classes; now a configuration default.
    /// </summary>
    private static int GetDefaultThrottleMs(string providerName) => providerName switch
    {
        "apple_books_ebook"     => 300,
        "apple_books_audiobook" => 300,
        "wikidata"              => 1100,  // Wikidata 1 req/sec policy
        _                      => 0,     // No throttle by default
    };

    /// <summary>
    /// Returns the default max concurrency for a known provider name.
    /// Most providers default to serial (1); override for providers that
    /// allow parallel calls.
    /// </summary>
    private static int GetDefaultMaxConcurrency(string providerName) => providerName switch
    {
        _ => 1, // All providers default to serial
    };
}
