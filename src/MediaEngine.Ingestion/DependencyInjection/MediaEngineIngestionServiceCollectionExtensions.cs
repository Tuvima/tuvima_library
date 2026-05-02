using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Enums;
using MediaEngine.Ingestion.Contracts;
using MediaEngine.Ingestion.Models;
using MediaEngine.Ingestion.Services;
using MediaEngine.Processors;
using MediaEngine.Processors.Contracts;
using MediaEngine.Processors.Processors;
using MediaEngine.Providers.Services;
using MediaEngine.Storage.Contracts;
using MediaEngine.Storage.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace MediaEngine.Ingestion.DependencyInjection;

/// <summary>
/// Internal Tuvima Library registration surface for the ingestion pipeline.
/// The Engine remains the host; this library only contributes services.
/// </summary>
public static class MediaEngineIngestionServiceCollectionExtensions
{
    public static IServiceCollection AddMediaEngineIngestion(
        this IServiceCollection services,
        IConfiguration configuration,
        IConfigurationLoader configLoader,
        Action<MediaEngineIngestionRegistrationOptions>? configure = null)
    {
        var options = new MediaEngineIngestionRegistrationOptions();
        configure?.Invoke(options);

        if (options.ConfigureOptions)
            ConfigureIngestionOptions(services, configuration, configLoader);

        if (options.CreateConfiguredDirectories)
            EnsureConfiguredDirectories(configLoader);

        services.TryAddSingleton<ILibraryFolderResolver, LibraryFolderResolver>();
        services.TryAddSingleton<IInitialSweepService, InitialSweepService>();
        services.TryAddSingleton<IAssetHasher, AssetHasher>();
        services.TryAddSingleton<IFileWatcher, FileWatcher>();
        services.TryAddSingleton<DebounceQueue>();
        services.TryAddSingleton<IFileOrganizer, FileOrganizer>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IMetadataTagger, EpubMetadataTagger>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IMetadataTagger, AudioMetadataTagger>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IMetadataTagger, VideoMetadataTagger>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IMetadataTagger, ComicMetadataTagger>());
        services.TryAddSingleton<WritebackConfigState>();
        services.TryAddSingleton<IWriteBackService, WriteBackService>();
        services.TryAddSingleton<IBackgroundWorker, BackgroundWorker>();
        services.TryAddSingleton<IOrganizationGate, OrganizationGate>();
        services.TryAddSingleton<IAutoOrganizeService, AutoOrganizeService>();
        services.TryAddSingleton<ILibraryScanner, LibraryScanner>();
        services.TryAddSingleton<IEnrichmentConcurrencyLimiter>(sp =>
            new EnrichmentConcurrencyLimiter(
                sp.GetRequiredService<IConfigurationLoader>().LoadHydration(),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<EnrichmentConcurrencyLimiter>>()));

        services.TryAddSingleton<IProcessorRouter>(sp =>
        {
            var router = new MediaProcessorRouter();
            router.Register(new EpubProcessor());
            router.Register(new AudioProcessor());
            router.Register(new VideoProcessor(sp.GetRequiredService<IVideoMetadataExtractor>()));
            router.Register(new ComicProcessor());
            router.Register(new GenericFileProcessor());
            return router;
        });

        services.TryAddSingleton<IngestionEngine>();
        services.TryAddSingleton<IIngestionEngine>(sp => sp.GetRequiredService<IngestionEngine>());

        if (options.RegisterHostedService)
        {
            services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<IngestionEngine>());
        }

        return services;
    }

    private static void ConfigureIngestionOptions(
        IServiceCollection services,
        IConfiguration configuration,
        IConfigurationLoader configLoader)
    {
        services.Configure<IngestionOptions>(configuration.GetSection(IngestionOptions.SectionName));

        services.PostConfigure<IngestionOptions>(opts =>
        {
            string? envWatch = Environment.GetEnvironmentVariable("TUVIMA_WATCH_FOLDER");
            string? envLibrary = Environment.GetEnvironmentVariable("TUVIMA_LIBRARY_ROOT");
            if (!string.IsNullOrWhiteSpace(envWatch)) opts.WatchDirectory = envWatch;
            if (!string.IsNullOrWhiteSpace(envLibrary)) opts.LibraryRoot = envLibrary;

            try
            {
                CoreConfiguration core = configLoader.LoadCore();
                if (!string.IsNullOrWhiteSpace(core.WatchDirectory)) opts.WatchDirectory = core.WatchDirectory;
                if (!string.IsNullOrWhiteSpace(core.LibraryRoot))
                {
                    opts.LibraryRoot = core.LibraryRoot;
                    opts.AutoOrganize = true;
                }
                if (!string.IsNullOrWhiteSpace(core.OrganizationTemplate)) opts.OrganizationTemplate = core.OrganizationTemplate;
                if (core.OrganizationTemplates.Count > 0)
                    opts.OrganizationTemplates = new Dictionary<string, string>(core.OrganizationTemplates, StringComparer.OrdinalIgnoreCase);
                opts.ConfiguredLanguage = core.Language.Metadata;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[WARN] Failed to load core configuration for ingestion options - using defaults: {ex.Message}");
            }

            try
            {
                var disambiguation = configLoader.LoadDisambiguation();
                opts.MediaTypeAutoAssignThreshold = disambiguation.MediaTypeAutoAssignThreshold;
                opts.MediaTypeReviewThreshold = disambiguation.MediaTypeReviewThreshold;
            }
            catch
            {
                // First run - defaults from IngestionOptions stand.
            }

            try
            {
                var libraries = configLoader.LoadLibraries();
                opts.LibraryFolders = libraries.Libraries
                    .Select(l =>
                    {
                        var paths = (l.SourcePaths is { Count: > 0 } ? l.SourcePaths : [])
                            .Concat(string.IsNullOrWhiteSpace(l.SourcePath) ? [] : [l.SourcePath])
                            .Where(p => !string.IsNullOrWhiteSpace(p))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList();

                        return new LibraryFolderEntry
                        {
                            SourcePath = paths.FirstOrDefault() ?? string.Empty,
                            SourcePaths = paths,
                            MediaTypes = l.MediaTypes
                                .Select(ParseMediaTypeFromConfig)
                                .Where(mt => mt != MediaType.Unknown)
                                .ToList(),
                            ReadOnly = l.ReadOnly,
                            WritebackOverride = l.WritebackOverride,
                        };
                    })
                    .Where(e => e.EffectiveSourcePaths.Count > 0 && e.MediaTypes.Count > 0)
                    .ToList();

                LibraryFolderResolver.ValidateNoOverlap(opts.LibraryFolders);
            }
            catch (InvalidOperationException ex)
            {
                Console.Error.WriteLine($"[ERROR] config/libraries.json: {ex.Message}");
                throw;
            }
            catch
            {
                // No libraries.json or parse failure - library folder priors will not be applied.
            }
        });
    }

    private static void EnsureConfiguredDirectories(IConfigurationLoader configLoader)
    {
        try
        {
            var libsForSubfolders = configLoader.LoadLibraries();
            string[] mediaTypeSubfolders = ["Books", "Audiobooks", "Movies", "TV", "Music", "Comics"];
            foreach (var lib in libsForSubfolders.Libraries)
            {
                var allPaths = (lib.SourcePaths is { Count: > 0 } ? lib.SourcePaths : [])
                    .Concat(string.IsNullOrWhiteSpace(lib.SourcePath) ? [] : [lib.SourcePath])
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Distinct(StringComparer.OrdinalIgnoreCase);

                foreach (var path in allPaths)
                {
                    if (!Directory.Exists(path))
                        continue;

                    foreach (var sub in mediaTypeSubfolders)
                        Directory.CreateDirectory(Path.Combine(path, sub));
                }
            }
        }
        catch
        {
            // No libraries.json or directory creation failed - non-fatal.
        }

        try
        {
            var coreForDataDir = configLoader.LoadCore();
            if (string.IsNullOrWhiteSpace(coreForDataDir.LibraryRoot))
                return;

            Directory.CreateDirectory(Path.Combine(coreForDataDir.LibraryRoot, ".data", "staging"));
            Directory.CreateDirectory(Path.Combine(coreForDataDir.LibraryRoot, ".data", "assets", "artwork"));
            Directory.CreateDirectory(Path.Combine(coreForDataDir.LibraryRoot, ".data", "assets", "derived"));
            Directory.CreateDirectory(Path.Combine(coreForDataDir.LibraryRoot, ".data", "assets", "metadata"));
            Directory.CreateDirectory(Path.Combine(coreForDataDir.LibraryRoot, ".data", "assets", "transcripts"));
            Directory.CreateDirectory(Path.Combine(coreForDataDir.LibraryRoot, ".data", "assets", "subtitle-cache"));
            Directory.CreateDirectory(Path.Combine(coreForDataDir.LibraryRoot, ".data", "assets", "text-tracks"));
            Directory.CreateDirectory(Path.Combine(coreForDataDir.LibraryRoot, ".data", "assets", "people"));
            Directory.CreateDirectory(Path.Combine(coreForDataDir.LibraryRoot, ".data", "database"));
        }
        catch
        {
            // LibraryRoot not yet configured or directory creation failed - non-fatal.
        }
    }

    private static MediaType ParseMediaTypeFromConfig(string configValue)
    {
        if (Enum.TryParse<MediaType>(configValue, ignoreCase: true, out var mt))
            return mt;

        return configValue.ToLowerInvariant() switch
        {
            "epub" => MediaType.Books,
            "ebook" => MediaType.Books,
            "audiobook" => MediaType.Audiobooks,
            "comics" => MediaType.Comics,
            "movie" => MediaType.Movies,
            _ => MediaType.Unknown,
        };
    }
}

public sealed class MediaEngineIngestionRegistrationOptions
{
    public bool ConfigureOptions { get; set; } = true;
    public bool CreateConfiguredDirectories { get; set; } = true;
    public bool RegisterHostedService { get; set; } = true;
}
