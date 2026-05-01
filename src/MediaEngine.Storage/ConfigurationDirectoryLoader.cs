using System.Text.Json;
using MediaEngine.Domain;
using MediaEngine.Domain.Models;
using MediaEngine.Storage.Contracts;
using MediaEngine.Storage.Models;

namespace MediaEngine.Storage;

/// <summary>
/// Multi-file configuration loader that reads and writes individual JSON files
/// from a structured directory layout.
///
/// <para>
/// <b>Directory layout:</b>
/// <code>
/// config/
///   core.json                 ← CoreConfiguration
///   scoring.json              ← ScoringSettings
///   maintenance.json          ← MaintenanceSettings
///   providers/                ← Per-provider ProviderConfiguration
///     local_filesystem.json
///     apple_api.json
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
///       manifest file is found, it is automatically split into the
///       directory structure and renamed to <c>.migrated</c>.</item>
/// <item>If neither exists (first run), a default directory is created
///       with sensible defaults for all configuration files.</item>
/// </list>
/// </para>
///
/// <para>
/// Implements both <see cref="IConfigurationLoader"/> (granular access)
/// and <see cref="IStorageManifest"/> (backward compatibility). The
/// <see cref="IStorageManifest.Load"/> method assembles all individual
/// files into a composite <see cref="LegacyManifest"/>.
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

    // ── File names ────────────────────────────────────────────────────────────

    private const string CoreFileName        = "core.json";
    private const string ScoringFileName     = "scoring.json";
    private const string MaintenanceFileName = "maintenance.json";
    private const string HydrationFileName   = "hydration.json";
    private const string MediaTypesFileName       = "media_types.json";
    private const string DisambiguationFileName   = "disambiguation.json";
    private const string TranscodingFileName      = "transcoding.json";
    private const string FieldPrioritiesFileName  = "field_priorities.json";
    private const string PipelinesFileName        = "pipelines.json";

    // ── Endpoint distribution map for legacy migration ────────────────────────

    /// <summary>
    /// Maps legacy <c>provider_endpoints</c> keys to the provider names that
    /// should receive them during migration from a legacy manifest file.
    /// </summary>
    private static readonly Dictionary<string, string[]> EndpointToProviders = new(StringComparer.OrdinalIgnoreCase)
    {
        ["apple_api"]       = ["apple_api"],
        ["wikidata_api"]    = ["wikidata"],
        ["wikidata_sparql"] = ["wikidata"],
    };

    // ── Constructor ───────────────────────────────────────────────────────────

    /// <param name="configDirectoryPath">
    /// Root directory for all configuration files (e.g. <c>"config"</c>).
    /// </param>
    /// <param name="legacyManifestPath">
    /// Optional path to a legacy manifest file.
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

    /// <inheritdoc/>
    public string ConfigDirectoryPath => _configDir;

    // ═══════════════════════════════════════════════════════════════════════════
    //  IStorageManifest — Backward Compatibility
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Assembles all individual configuration files into a composite
    /// <see cref="LegacyManifest"/>. Existing consumers that depend
    /// on the monolithic manifest shape continue to work unchanged.
    /// </summary>
    public LegacyManifest Load()
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

        return new LegacyManifest
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
    /// Distributes a composite <see cref="LegacyManifest"/> back into
    /// individual configuration files. Used by legacy consumers that still call
    /// <see cref="IStorageManifest.Save"/>.
    /// </summary>
    public void Save(LegacyManifest manifest)
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
        LoadFile<CoreConfiguration>(CoreFileName)
        ?? new();

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
    public HydrationSettings LoadHydration() =>
        LoadFile<HydrationSettings>(HydrationFileName) ?? new();

    /// <inheritdoc/>
    public void SaveHydration(HydrationSettings settings) =>
        SaveFile(HydrationFileName, settings);

    /// <inheritdoc/>
    public DisambiguationSettings LoadDisambiguation() =>
        LoadFile<DisambiguationSettings>(DisambiguationFileName) ?? new();

    /// <inheritdoc/>
    public void SaveDisambiguation(DisambiguationSettings settings) =>
        SaveFile(DisambiguationFileName, settings);

    /// <inheritdoc/>
    public TranscodingSettings LoadTranscoding() =>
        LoadFile<TranscodingSettings>(TranscodingFileName) ?? new();

    /// <inheritdoc/>
    public void SaveTranscoding(TranscodingSettings settings) =>
        SaveFile(TranscodingFileName, settings);

    /// <inheritdoc/>
    public FieldPriorityConfiguration LoadFieldPriorities() =>
        LoadFile<FieldPriorityConfiguration>(FieldPrioritiesFileName) ?? new();

    /// <inheritdoc/>
    public void SaveFieldPriorities(FieldPriorityConfiguration config) =>
        SaveFile(FieldPrioritiesFileName, config);

    // ── UI Palette ───────────────────────────────────────────────────────────

    private const string UiSubdir      = "ui";
    private const string PaletteFileName = "palette.json";

    /// <inheritdoc/>
    public PaletteConfiguration LoadPalette() =>
        LoadFile<PaletteConfiguration>(Path.Combine(UiSubdir, PaletteFileName)) ?? new();

    /// <inheritdoc/>
    public void SavePalette(PaletteConfiguration palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        SaveFile(Path.Combine(UiSubdir, PaletteFileName), palette);
    }

    // ── AI Settings ──────────────────────────────────────────────────────────

    private const string AiFileName = "ai.json";

    /// <inheritdoc/>
    public T? LoadAi<T>() where T : class =>
        LoadFile<T>(AiFileName);

    /// <inheritdoc/>
    public void SaveAi<T>(T settings) where T : class
    {
        ArgumentNullException.ThrowIfNull(settings);
        SaveFile(AiFileName, settings);
    }

    // ── Libraries ────────────────────────────────────────────────────────────

    private const string LibrariesFileName = "libraries.json";

    /// <inheritdoc/>
    public LibrariesConfiguration LoadLibraries() =>
        LoadFile<LibrariesConfiguration>(LibrariesFileName) ?? new();

    /// <inheritdoc/>
    public PipelineConfiguration LoadPipelines()
    {
        var pipelines = LoadFile<Dictionary<string, MediaTypePipeline>>(PipelinesFileName);
        if (pipelines is not null && pipelines.Count > 0)
            return new PipelineConfiguration { Pipelines = new Dictionary<string, MediaTypePipeline>(pipelines, StringComparer.OrdinalIgnoreCase) };

        return new PipelineConfiguration();
    }

    /// <inheritdoc/>
    public void SavePipelines(PipelineConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(config);
        // Store as a flat dictionary (matching pipelines.json format).
        SaveFile(PipelinesFileName, config.Pipelines);
    }

    /// <inheritdoc/>
    public MediaTypeConfiguration LoadMediaTypes() =>
        LoadFile<MediaTypeConfiguration>(MediaTypesFileName)
        ?? new MediaTypeConfiguration();

    /// <inheritdoc/>
    public void SaveMediaTypes(MediaTypeConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(config);
        SaveFile(MediaTypesFileName, config);
    }

    /// <inheritdoc/>
    public ProviderConfiguration? LoadProvider(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var config = LoadFile<ProviderConfiguration>(Path.Combine(ProvidersSubdir, $"{name}.json"));
        if (config is not null)
            ApplySecrets(config, name);
        return config;
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

        // Auto-migrate: if old split Apple Books configs exist but merged one does not,
        // create the merged config and rename old files.
        MigrateAppleBooksIfNeeded(dir);

        var results = new List<ProviderConfiguration>();
        foreach (var file in Directory.EnumerateFiles(dir, "*.json"))
        {
            var config = TryDeserializeFile<ProviderConfiguration>(file);
            if (config is not null)
            {
                ApplySecrets(config, Path.GetFileNameWithoutExtension(file));
                results.Add(config);
            }
        }
        return results;
    }

    /// <summary>
    /// Overlays secrets from <c>config/secrets/{providerName}.json</c> onto a loaded
    /// provider configuration. Secrets files contain flat JSON objects with keys like
    /// <c>api_key</c>, <c>username</c>, <c>password</c> that are merged into the
    /// provider's <see cref="HttpClientConfig"/>.
    /// </summary>
    private void ApplySecrets(ProviderConfiguration config, string providerName)
    {
        var secretsPath = Path.Combine(_configDir, "secrets", $"{providerName}.json");
        if (!File.Exists(secretsPath))
            return;

        try
        {
            var json = File.ReadAllText(secretsPath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
                return;

            // Ensure HttpClient exists if secrets reference it
            if (config.HttpClient is null)
            {
                var hasHttpField = root.TryGetProperty("api_key", out _)
                    || root.TryGetProperty("username", out _)
                    || root.TryGetProperty("password", out _);

                if (hasHttpField)
                    config.HttpClient = new HttpClientConfig();
            }

            if (config.HttpClient is not null)
            {
                if (root.TryGetProperty("api_key", out var apiKey) && apiKey.ValueKind == JsonValueKind.String)
                    config.HttpClient.ApiKey = apiKey.GetString();
                if (root.TryGetProperty("username", out var user) && user.ValueKind == JsonValueKind.String)
                    config.HttpClient.Username = user.GetString();
                if (root.TryGetProperty("password", out var pass) && pass.ValueKind == JsonValueKind.String)
                    config.HttpClient.Password = pass.GetString();
            }
        }
        catch
        {
            // Silently skip malformed secrets files
        }
    }

    /// <summary>
    /// If old split Apple Books configs (apple_books_ebook.json, apple_books_audiobook.json) exist
    /// but the merged apple_api.json does not, creates the merged config and renames old files.
    /// </summary>
    private static void MigrateAppleBooksIfNeeded(string providerDir)
    {
        var mergedPath   = Path.Combine(providerDir, "apple_api.json");
        var ebookPath    = Path.Combine(providerDir, "apple_books_ebook.json");
        var audiobookPath = Path.Combine(providerDir, "apple_books_audiobook.json");

        if (File.Exists(mergedPath))
            return; // Already migrated

        if (!File.Exists(ebookPath) && !File.Exists(audiobookPath))
            return; // Nothing to migrate

        // Write the merged config with media-type-scoped strategies.
        var merged = new ProviderConfiguration
        {
            Name           = "apple_api",
            Version        = "2.0",
            Enabled        = true,
            Weight         = 0.7,
            Domain         = ProviderDomain.Universal,
            DisplayName    = "Apple API",
            AdapterType    = "config_driven",
            ProviderId     = WellKnownProviders.AppleApi.ToString(),
            CapabilityTags = ["cover", "title", "author", "description", "genre"],
            AvailableFields = ["title", "author", "cover", "description", "genre", "rating", "apple_books_id"],
            FieldWeights   = new() { ["cover"] = 0.85, ["title"] = 0.7, ["author"] = 0.7, ["description"] = 0.85, ["genre"] = 0.7, ["rating"] = 0.7 },
            Endpoints      = new() { ["api"] = "https://itunes.apple.com" },
            ThrottleMs     = 300,
            MaxConcurrency = 1,
            HydrationStages = [1],
            CanHandle      = new() { MediaTypes = ["Books", "Audiobooks"], EntityTypes = ["Work", "MediaAsset"] },
            HttpClient     = new() { TimeoutSeconds = 10 },
        };

        var json = JsonSerializer.Serialize(merged, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        });
        File.WriteAllText(mergedPath, json);

        // Rename old files so they are no longer loaded.
        if (File.Exists(ebookPath))
            File.Move(ebookPath, ebookPath + ".migrated", overwrite: true);
        if (File.Exists(audiobookPath))
            File.Move(audiobookPath, audiobookPath + ".migrated", overwrite: true);
    }

    /// <inheritdoc/>
    public T? LoadConfig<T>(string subdirectory, string name) where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var relativePath = string.IsNullOrWhiteSpace(subdirectory)
            ? $"{name}.json"
            : Path.Combine(subdirectory, $"{name}.json");
        return LoadFile<T>(relativePath);
    }

    /// <inheritdoc/>
    public void SaveConfig<T>(string subdirectory, string name, T config) where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(config);
        var relativePath = string.IsNullOrWhiteSpace(subdirectory)
            ? $"{name}.json"
            : Path.Combine(subdirectory, $"{name}.json");
        SaveFile(relativePath, config);
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
    /// Splits a legacy manifest file into the multi-file
    /// directory structure. Renames the legacy file to <c>.migrated</c>
    /// after successful migration.
    /// </summary>
    private void MigrateFromLegacy(string legacyPath)
    {
        LegacyManifest? manifest = null;
        try
        {
            var json = File.ReadAllText(legacyPath);
            manifest = JsonSerializer.Deserialize<LegacyManifest>(json, JsonOptions);
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
        Directory.CreateDirectory(Path.Combine(_configDir, "ui"));
        Directory.CreateDirectory(Path.Combine(_configDir, "ui", "devices"));
        Directory.CreateDirectory(Path.Combine(_configDir, "ui", "profiles"));

        // Core defaults
        SaveCore(new CoreConfiguration());
        SaveScoring(new ScoringSettings());
        SaveMaintenance(new MaintenanceSettings());
        SaveDisambiguation(new DisambiguationSettings());
        SaveFieldPriorities(new FieldPriorityConfiguration
        {
            FieldOverrides = new(StringComparer.OrdinalIgnoreCase)
            {
                ["description"] = new() { Priority = ["Wikidata Reconciliation", "apple_api", "google_books", "open_library", "tmdb"], Note = "Prefer rich Wikipedia descriptions from the reconciliation provider, then fall back to retail descriptions" },
                ["biography"]   = new() { Priority = ["Wikidata Reconciliation"], Note = "Rich Wikipedia bios for persons" },
                ["cover"]       = new() { Priority = ["apple_api", "tmdb", "open_library", "google_books", "musicbrainz"], Note = "Retail providers have high-res commercial art" },
                ["rating"]      = new() { Priority = ["apple_api", "tmdb"], Note = "Wikidata does not carry ratings" },
            }
        });
        SaveMediaTypes(new MediaTypeConfiguration());

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
            Name           = "apple_api",
            Enabled        = true,
            Weight         = 0.7,
            Domain         = ProviderDomain.Universal,
            CapabilityTags = ["cover", "description", "rating"],
            FieldWeights   = new() { ["cover"] = 0.85, ["description"] = 0.85, ["rating"] = 0.7 },
            Endpoints      = new() { ["api"] = "https://itunes.apple.com" },
            ThrottleMs     = 300,
        });

        SaveProvider(new ProviderConfiguration
        {
            Name           = "open_library",
            Enabled        = true,
            Weight         = 0.7,
            Domain         = ProviderDomain.Ebook,
            CapabilityTags = ["title", "author", "cover", "isbn", "year", "series"],
            FieldWeights   = new()
            {
                ["title"] = 0.75, ["author"] = 0.8, ["cover"] = 0.7,
                ["isbn"] = 0.9, ["year"] = 0.85, ["series"] = 0.9,
            },
            Endpoints      = new() { ["open_library"] = "https://openlibrary.org" },
            ThrottleMs     = 500,
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
        SavePalette(new PaletteConfiguration());
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
                IntentDockItems = ["Collections", "Watch", "Read", "Listen"],
                IntentDockStyle = "normal",
            },
            Pages = new UIPageSettings
            {
                Home = new UIHomePageSettings
                {
                    CollectionHeroLayout       = "stacked",
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
                IntentDockItems = ["Collections", "Watch", "Read", "Listen"],
                IntentDockStyle = "oversized",
            },
            Pages = new UIPageSettings
            {
                Home = new UIHomePageSettings
                {
                    CollectionHeroLayout       = "two-column-oversized",
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
                IntentDockItems = ["Collections", "Listen"],
                IntentDockStyle = "oversized",
            },
            Pages = new UIPageSettings
            {
                Home = new UIHomePageSettings
                {
                    CollectionHeroEnabled      = false,
                    CollectionHeroLayout       = "hidden",
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
        "apple_api"             => 300,
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
