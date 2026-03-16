using MediaEngine.Api.Models;
using MediaEngine.Api.Security;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Events;
using MediaEngine.Ingestion.Contracts;
using MediaEngine.Providers.Adapters;
using MediaEngine.Providers.Contracts;
using MediaEngine.Providers.Models;
using MediaEngine.Storage.Contracts;
using MediaEngine.Storage.Models;

namespace MediaEngine.Api.Endpoints;

/// <summary>
/// Settings endpoints for folder configuration, path testing, and provider status.
/// All routes are grouped under <c>/settings</c>.
///
/// Access:
///   Folders, test-path, organization-template — Administrator only.
///   Providers (read) — Administrator or Curator.
///   Providers (write) — Administrator only.
///
/// <list type="bullet">
///   <item><c>GET    /settings/folders</c>   — current Watch Folder + Library Folder</item>
///   <item><c>PUT    /settings/folders</c>   — save paths to manifest + hot-swap FileSystemWatcher</item>
///   <item><c>POST   /settings/test-path</c> — probe a path for existence / read / write access</item>
///   <item><c>GET    /settings/providers</c> — enabled state + async reachability for each provider</item>
/// </list>
/// </summary>
public static class SettingsEndpoints
{
    // Maps provider name → human-readable display label.
    private static readonly IReadOnlyDictionary<string, string> _displayNames =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["apple_books"]           = "Apple API",
            ["apple_api"]             = "Apple API",
            // audnexus removed - config file deleted as part of SPARQL cleanup
            ["wikidata"]              = "Wikidata",
            ["local_filesystem"]      = "Local Filesystem",
            ["open_library"]          = "Open Library",
            ["google_books"]          = "Google Books",
            ["tmdb"]                  = "TMDB",
            ["comic_vine"]            = "Comic Vine",
            ["musicbrainz"]           = "MusicBrainz",
        };

    // Maps provider name → key in manifest.ProviderEndpoints for the reachability probe.
    private static readonly IReadOnlyDictionary<string, string> _endpointKeys =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["apple_books"]           = "apple_books",
            ["apple_api"]             = "apple_api",
            // audnexus removed - config file deleted as part of SPARQL cleanup
            ["wikidata"]              = "wikidata_api",
            ["open_library"]          = "open_library",
            ["google_books"]          = "google_books",
            ["tmdb"]                  = "tmdb",
            ["comic_vine"]            = "comic_vine",
            ["musicbrainz"]           = "musicbrainz",
        };

    public static IEndpointRouteBuilder MapSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/settings").WithTags("Settings");

        // ── GET /settings/folders ──────────────────────────────────────────────

        grp.MapGet("/folders", (IConfigurationLoader configLoader) =>
        {
            var core = configLoader.LoadCore();
            return Results.Ok(new FolderSettingsResponse
            {
                WatchDirectory = core.WatchDirectory,
                LibraryRoot    = core.LibraryRoot,
            });
        })
        .WithName("GetFolderSettings")
        .WithSummary("Returns the currently configured Watch Folder and Library Folder paths.")
        .Produces<FolderSettingsResponse>(StatusCodes.Status200OK)
        .RequireAdmin();

        // ── PUT /settings/folders ──────────────────────────────────────────────

        grp.MapPut("/folders", async (
            UpdateFoldersRequest request,
            IConfigurationLoader configLoader,
            IFileWatcher         fileWatcher,
            IIngestionEngine     ingestionEngine,
            IEventPublisher      publisher,
            CancellationToken    ct) =>
        {
            // Path traversal validation.
            if (!string.IsNullOrWhiteSpace(request.WatchDirectory))
            {
                var err = PathValidator.Validate(request.WatchDirectory);
                if (err is not null)
                    return Results.BadRequest(new { error = err });
            }
            if (!string.IsNullOrWhiteSpace(request.LibraryRoot))
            {
                var err = PathValidator.Validate(request.LibraryRoot);
                if (err is not null)
                    return Results.BadRequest(new { error = err });
            }
            var core = configLoader.LoadCore();

            if (!string.IsNullOrWhiteSpace(request.WatchDirectory))
                core.WatchDirectory = request.WatchDirectory;

            if (!string.IsNullOrWhiteSpace(request.LibraryRoot))
                core.LibraryRoot = request.LibraryRoot;

            configLoader.SaveCore(core);

            // Hot-swap the FileSystemWatcher when the watch directory is provided and accessible.
            // Wrapped in try/catch because the watcher may not have been started yet in the
            // API process — the config save is the durable side-effect that matters.
            if (!string.IsNullOrWhiteSpace(request.WatchDirectory)
                && Directory.Exists(request.WatchDirectory))
            {
                try { fileWatcher.UpdateDirectory(request.WatchDirectory); }
                catch (Exception) { /* non-fatal: watcher swap failed; path is persisted to config. */ }

                // Scan existing files in the new watch directory so files that were
                // already present before the hot-swap are picked up.  Duplicates are
                // harmless — the pipeline's hash check short-circuits them.
                try { ingestionEngine.ScanDirectory(request.WatchDirectory); }
                catch (Exception) { /* non-fatal */ }
            }

            // Broadcast the new active watch path to all connected Dashboard circuits.
            await publisher.PublishAsync(
                "WatchFolderActive",
                new WatchFolderActiveEvent(core.WatchDirectory, DateTimeOffset.UtcNow),
                ct);

            return Results.Ok();
        })
        .WithName("UpdateFolderSettings")
        .WithSummary("Saves Watch Folder + Library Folder paths and hot-swaps the FileSystemWatcher.")
        .Produces(StatusCodes.Status200OK)
        .RequireAdmin();

        // ── POST /settings/test-path ────────────────────────────────────────────

        grp.MapPost("/test-path", (TestPathRequest request) =>
        {
            var path   = request.Path ?? string.Empty;

            // Path traversal validation.
            var pathError = PathValidator.Validate(path);
            if (pathError is not null)
                return Results.BadRequest(new { error = pathError });

            var exists = Directory.Exists(path);
            bool hasRead  = false;
            bool hasWrite = false;

            if (exists)
            {
                // Read probe: attempt to enumerate at least one entry.
                try
                {
                    // ReSharper disable once ReturnValueOfPureMethodIsNotUsed — intentional probe.
                    Directory.EnumerateFileSystemEntries(path).Any();
                    hasRead = true;
                }
                catch { /* access denied or I/O error */ }

                // Write probe: create and immediately delete a sentinel file.
                try
                {
                    var probe = Path.Combine(path, $".tuvima_probe_{Guid.NewGuid():N}");
                    File.WriteAllText(probe, string.Empty);
                    File.Delete(probe);
                    hasWrite = true;
                }
                catch { /* read-only file system or access denied */ }
            }

            return Results.Ok(new TestPathResponse
            {
                Path     = path,
                Exists   = exists,
                HasRead  = hasRead,
                HasWrite = hasWrite,
            });
        })
        .WithName("TestPath")
        .WithSummary("Probes a directory path for existence, read access, and write access.")
        .Produces<TestPathResponse>(StatusCodes.Status200OK)
        .RequireAdmin();

        // ── POST /settings/browse-directory ────────────────────────────────────────

        grp.MapPost("/browse-directory", (BrowseDirectoryRequest request) =>
        {
            var path = request.Path?.Trim();

            // Empty/null → list drive roots.
            if (string.IsNullOrEmpty(path))
            {
                var drives = DriveInfo.GetDrives()
                    .Where(d => d.IsReady)
                    .Select(d => d.Name)
                    .Order()
                    .ToList();

                return Results.Ok(new BrowseDirectoryResponse
                {
                    CurrentPath = string.Empty,
                    ParentPath  = null,
                    Directories = drives,
                });
            }

            // Path traversal validation.
            var pathError = PathValidator.Validate(path);
            if (pathError is not null)
                return Results.BadRequest(new { error = pathError });

            if (!Directory.Exists(path))
                return Results.Ok(new BrowseDirectoryResponse
                {
                    CurrentPath = path,
                    ParentPath  = Path.GetDirectoryName(path),
                    Directories = [],
                });

            List<string> dirs;
            try
            {
                dirs = Directory.GetDirectories(path)
                    .Select(Path.GetFileName)
                    .Where(n => n is not null && !n.StartsWith('.'))
                    .Select(n => n!)
                    .Order()
                    .ToList();
            }
            catch
            {
                dirs = [];
            }

            return Results.Ok(new BrowseDirectoryResponse
            {
                CurrentPath = path,
                ParentPath  = Path.GetDirectoryName(path),
                Directories = dirs,
            });
        })
        .WithName("BrowseDirectory")
        .WithSummary("Lists subdirectories at the given path, or drive roots if path is empty.")
        .Produces<BrowseDirectoryResponse>(StatusCodes.Status200OK)
        .RequireAdmin();

        // ── PUT /settings/providers/{name} ───────────────────────────────────────

        grp.MapPut("/providers/{name}", (
            string               name,
            UpdateProviderRequest request,
            IConfigurationLoader configLoader) =>
        {
            var provider = configLoader.LoadProvider(name);

            if (provider is null)
                return Results.NotFound(new { error = $"Provider '{name}' not found." });

            provider.Enabled = request.Enabled;
            configLoader.SaveProvider(provider);

            var displayName = ResolveDisplayName(provider);

            return Results.Ok(BuildProviderStatusResponse(provider, displayName));
        })
        .WithName("UpdateProvider")
        .WithSummary("Toggles a provider's enabled state and saves to the manifest.")
        .Produces<ProviderStatusResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAdmin();

        // ── GET /settings/providers ─────────────────────────────────────────────

        grp.MapGet("/providers", async (
            IConfigurationLoader configLoader,
            IHttpClientFactory   httpFactory,
            CancellationToken    ct) =>
        {
            var providers = configLoader.LoadAllProviders();
            var http      = httpFactory.CreateClient("settings_probe");

            // Check each provider's reachability concurrently.
            var statusTasks = providers.Select(async provider =>
            {
                var name        = provider.Name;
                var displayName = ResolveDisplayName(provider);
                bool isReachable = false;

                var baseUrl = GetBaseUrlForProvider(provider);
                if (provider.Enabled && !string.IsNullOrWhiteSpace(baseUrl))
                {
                    using var probeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    probeCts.CancelAfter(TimeSpan.FromSeconds(3));
                    try
                    {
                        using var req  = new HttpRequestMessage(HttpMethod.Get, baseUrl);
                        using var resp = await http.SendAsync(
                            req, HttpCompletionOption.ResponseHeadersRead, probeCts.Token);
                        // Any response (even 4xx) confirms the server is reachable.
                        // Only connection-level failures mean unreachable.
                        isReachable = (int)resp.StatusCode < 500;
                    }
                    catch { /* timeout / DNS failure / network error — isReachable stays false */ }
                }

                return BuildProviderStatusResponse(provider, displayName, isReachable);
            });

            return Results.Ok(await Task.WhenAll(statusTasks));
        })
        .WithName("GetProviderStatus")
        .WithSummary("Returns enabled/reachability status for all registered metadata providers.")
        .Produces<ProviderStatusResponse[]>(StatusCodes.Status200OK)
        .RequireAdminOrCurator();

        // ── GET /settings/organization-template ───────────────────────────────────

        grp.MapGet("/organization-template", (
            IConfigurationLoader                          configLoader,
            Microsoft.Extensions.Options.IOptions<MediaEngine.Ingestion.Models.IngestionOptions> ingestionOpts,
            IFileOrganizer                                organizer) =>
        {
            var core = configLoader.LoadCore();
            string template = !string.IsNullOrWhiteSpace(core.OrganizationTemplate)
                ? core.OrganizationTemplate
                : ingestionOpts.Value.OrganizationTemplate;

            string? preview = organizer.ValidateTemplate(template, out _);

            return Results.Ok(new OrganizationTemplateResponse
            {
                Template  = template,
                Preview   = preview,
                Templates = core.OrganizationTemplates,
            });
        })
        .WithName("GetOrganizationTemplate")
        .WithSummary("Returns the current file organization templates (default + per-media-type) and a sample preview.")
        .Produces<OrganizationTemplateResponse>(StatusCodes.Status200OK)
        .RequireAdmin();

        // ── PUT /settings/organization-template ───────────────────────────────────

        grp.MapPut("/organization-template", (
            UpdateOrganizationTemplateRequest request,
            IConfigurationLoader              configLoader,
            IFileOrganizer                    organizer) =>
        {
            if (string.IsNullOrWhiteSpace(request.Template))
                return Results.BadRequest(new { error = "Template cannot be empty." });

            string? preview = organizer.ValidateTemplate(request.Template, out var error);
            if (preview is null)
                return Results.BadRequest(new { error = error ?? "Invalid template." });

            // Validate per-media-type templates if provided.
            if (request.Templates is not null)
            {
                foreach (var (key, tmpl) in request.Templates)
                {
                    if (string.IsNullOrWhiteSpace(tmpl)) continue;
                    string? typePreview = organizer.ValidateTemplate(tmpl, out var typeError);
                    if (typePreview is null)
                        return Results.BadRequest(new { error = $"Invalid template for '{key}': {typeError}" });
                }
            }

            var core = configLoader.LoadCore();
            core.OrganizationTemplate = request.Template;
            if (request.Templates is not null)
                core.OrganizationTemplates = new Dictionary<string, string>(request.Templates, StringComparer.OrdinalIgnoreCase);
            configLoader.SaveCore(core);

            return Results.Ok(new OrganizationTemplateResponse
            {
                Template  = request.Template,
                Preview   = preview,
                Templates = core.OrganizationTemplates,
            });
        })
        .WithName("UpdateOrganizationTemplate")
        .WithSummary("Validates and saves file organization templates (default + per-media-type).")
        .Produces<OrganizationTemplateResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .RequireAdmin();

        // ── POST /settings/providers/{name}/test ────────────────────────────────
        // Tests a provider by sending a real request with a known title and
        // returning success/failure, response time, and sample fields.

        grp.MapPost("/providers/{name}/test", async (
            string                                          name,
            IConfigurationLoader                            configLoader,
            IEnumerable<IExternalMetadataProvider>           providers,
            IHttpClientFactory                               httpFactory,
            ILoggerFactory                                   loggerFactory,
            CancellationToken                                ct) =>
        {
            var providerConfig = configLoader.LoadProvider(name);
            if (providerConfig is null)
                return Results.NotFound(new { error = $"Provider '{name}' not found." });

            // Find the registered adapter by name; fall back to constructing one
            // directly from config when DI lookup fails (e.g. Engine not restarted).
            var adapter = providers.FirstOrDefault(p =>
                string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

            if (adapter is null
                && !string.Equals(name, "wikidata", StringComparison.OrdinalIgnoreCase))
            {
                // Unconditionally attempt ConfigDrivenAdapter construction for any
                // non-Wikidata provider whose DI lookup failed. Catches constructor
                // errors gracefully — if the config isn't suitable the 404 below fires.
                try
                {
                    adapter = new ConfigDrivenAdapter(
                        providerConfig,
                        httpFactory,
                        loggerFactory.CreateLogger<ConfigDrivenAdapter>());
                }
                catch { /* Config not suitable for ConfigDrivenAdapter — fall through */ }
            }

            if (adapter is null)
                return Results.NotFound(new { error = $"No adapter registered for '{name}'. AdapterType='{providerConfig.AdapterType}', DI providers={string.Join(", ", providers.Select(p => p.Name))}." });

            // Build a test request with "The Fellowship of the Ring" as the sample title.
            var baseUrl = GetBaseUrlForProvider(providerConfig);
            var sparqlUrl = providerConfig.Endpoints.TryGetValue("wikidata_sparql", out var sp) ? sp : null;
            // Use domain-appropriate test data: audiobook providers need an audiobook
            // ASIN; ebook providers use the ebook ASIN. B0099ELYMS is the Audible
            // ASIN for The Fellowship of the Ring (Rob Inglis narration).
            var isAudiobook = providerConfig.Domain == ProviderDomain.Audiobook;
            var testRequest = new ProviderLookupRequest
            {
                EntityId    = Guid.NewGuid(),
                EntityType  = EntityType.Work,
                MediaType   = isAudiobook ? MediaType.Audiobooks : MediaType.Books,
                Title       = "The Fellowship of the Ring",
                Author      = "J.R.R. Tolkien",
                Isbn        = "9780547928210",
                Asin        = isAudiobook ? "B0099ELYMS" : "B007978NPG",
                BaseUrl     = baseUrl ?? string.Empty,
                SparqlBaseUrl = sparqlUrl,
            };

            var sw = System.Diagnostics.Stopwatch.StartNew();
            IReadOnlyList<ProviderClaim> claims;
            try
            {
                claims = await adapter.FetchAsync(testRequest, ct);
            }
            catch (Exception ex)
            {
                return Results.Ok(new ProviderTestResponse
                {
                    Success        = false,
                    ResponseTimeMs = (int)sw.ElapsedMilliseconds,
                    SampleFields   = [],
                    Message        = $"Test failed: {ex.Message}",
                });
            }
            sw.Stop();

            // Wikidata is a special case: reaching the API without an exception means
            // the connection works, even if the test title did not match any QID.
            var isWikidata = string.Equals(name, "wikidata", StringComparison.OrdinalIgnoreCase);
            var success = claims.Count > 0 || isWikidata;

            string message;
            if (claims.Count > 0)
                message = $"Success — {claims.Count} claims returned in {sw.ElapsedMilliseconds}ms.";
            else if (isWikidata)
                message = $"Connection verified ({sw.ElapsedMilliseconds}ms). No claims matched the test title — this is normal. Wikidata lookups depend on bridge identifiers from other providers.";
            else
                message = "Test returned zero claims. The provider may be unreachable or the test title was not found.";

            return Results.Ok(new ProviderTestResponse
            {
                Success        = success,
                ResponseTimeMs = (int)sw.ElapsedMilliseconds,
                SampleFields   = claims.Select(c => c.Key).Distinct().ToList(),
                Message        = message,
            });
        })
        .WithName("TestProvider")
        .WithSummary("Tests a provider with a sample title and returns success/failure and available fields.")
        .Produces<ProviderTestResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAdmin();

        // ── POST /settings/providers/{name}/sample ──────────────────────────────
        // Fetches sample claims from a provider for a given title.
        // Returns the full claim list for property picker UI.

        grp.MapPost("/providers/{name}/sample", async (
            string                                          name,
            ProviderSampleRequest                           request,
            IConfigurationLoader                            configLoader,
            IEnumerable<IExternalMetadataProvider>           providers,
            IHttpClientFactory                               httpFactory,
            ILoggerFactory                                   loggerFactory,
            CancellationToken                                ct) =>
        {
            var providerConfig = configLoader.LoadProvider(name);
            if (providerConfig is null)
                return Results.NotFound(new { error = $"Provider '{name}' not found." });

            // Find the registered adapter; fall back to constructing one directly
            // from config when DI lookup fails.
            var adapter = providers.FirstOrDefault(p =>
                string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

            if (adapter is null
                && !string.Equals(name, "wikidata", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    adapter = new ConfigDrivenAdapter(
                        providerConfig,
                        httpFactory,
                        loggerFactory.CreateLogger<ConfigDrivenAdapter>());
                }
                catch { /* Config not suitable for ConfigDrivenAdapter — fall through */ }
            }

            if (adapter is null)
                return Results.NotFound(new { error = $"No adapter registered for '{name}'. AdapterType='{providerConfig.AdapterType}', DI providers={string.Join(", ", providers.Select(p => p.Name))}." });

            var baseUrl = GetBaseUrlForProvider(providerConfig);
            var sparqlUrl = providerConfig.Endpoints.TryGetValue("wikidata_sparql", out var sp) ? sp : null;

            var mediaType = MediaType.Books; // Default.
            if (!string.IsNullOrWhiteSpace(request.MediaType)
                && Enum.TryParse<MediaType>(request.MediaType, true, out var parsed))
            {
                mediaType = parsed;
            }

            var lookup = new ProviderLookupRequest
            {
                EntityId      = Guid.NewGuid(),
                EntityType    = EntityType.Work,
                MediaType     = mediaType,
                Title         = request.Title ?? "The Fellowship of the Ring",
                Author        = request.Author,
                Isbn          = request.Isbn,
                Asin          = request.Asin,
                BaseUrl       = baseUrl ?? string.Empty,
                SparqlBaseUrl = sparqlUrl,
            };

            var claims = await adapter.FetchAsync(lookup, ct);

            return Results.Ok(new ProviderSampleResponse
            {
                ProviderName = name,
                Claims       = claims.Select(c => new ProviderSampleClaim
                {
                    Key        = c.Key,
                    Value      = c.Value.Length > 500 ? c.Value[..500] + "…" : c.Value,
                    Confidence = c.Confidence,
                }).ToList(),
            });
        })
        .WithName("SampleProvider")
        .WithSummary("Fetches sample claims from a provider for a given title, for the property picker UI.")
        .Produces<ProviderSampleResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAdmin();

        // ── PUT /settings/providers/{name}/config ───────────────────────────────
        // Saves the full provider configuration (endpoints, weights, throttle, etc.)

        grp.MapPut("/providers/{name}/config", (
            string                     name,
            ProviderConfigUpdateRequest request,
            IConfigurationLoader       configLoader) =>
        {
            var existing = configLoader.LoadProvider(name);
            if (existing is null)
                return Results.NotFound(new { error = $"Provider '{name}' not found." });

            // Update mutable fields.
            if (request.Enabled.HasValue)
                existing.Enabled = request.Enabled.Value;
            if (request.Weight.HasValue)
                existing.Weight = Math.Clamp(request.Weight.Value, 0.0, 1.0);
            if (request.FieldWeights is not null)
                existing.FieldWeights = request.FieldWeights;
            if (request.ThrottleMs.HasValue)
                existing.ThrottleMs = Math.Max(0, request.ThrottleMs.Value);
            if (request.MaxConcurrency.HasValue)
                existing.MaxConcurrency = Math.Max(1, request.MaxConcurrency.Value);
            if (request.Endpoints is not null)
            {
                foreach (var (key, url) in request.Endpoints)
                    existing.Endpoints[key] = url;
            }
            if (request.CapabilityTags is not null)
                existing.CapabilityTags = request.CapabilityTags;

            // Config-driven field mappings: replace the entire list if provided.
            if (request.FieldMappings is not null)
            {
                existing.FieldMappings = request.FieldMappings
                    .Select(fm => new MediaEngine.Storage.Models.FieldMappingConfig
                    {
                        ClaimKey      = fm.ClaimKey,
                        JsonPath      = fm.JsonPath,
                        Confidence    = fm.Confidence,
                        Transform     = fm.Transform,
                        TransformArgs = fm.TransformArgs,
                    })
                    .ToList();
            }

            // HTTP client settings: timeout and API key.
            if (request.TimeoutSeconds.HasValue)
            {
                existing.HttpClient ??= new MediaEngine.Storage.Models.HttpClientConfig();
                existing.HttpClient.TimeoutSeconds = Math.Clamp(request.TimeoutSeconds.Value, 1, 120);
            }
            if (request.ApiKey is not null)
            {
                existing.HttpClient ??= new MediaEngine.Storage.Models.HttpClientConfig();
                existing.HttpClient.ApiKey = request.ApiKey;
            }
            if (request.CustomIconName is not null)
                existing.CustomIconName = string.IsNullOrWhiteSpace(request.CustomIconName) ? null : request.CustomIconName;

            configLoader.SaveProvider(existing);

            var displayName = ResolveDisplayName(existing);

            return Results.Ok(BuildProviderStatusResponse(existing, displayName));
        })
        .WithName("UpdateProviderConfig")
        .WithSummary("Saves full provider configuration including endpoints, weights, throttle, and capabilities.")
        .Produces<ProviderStatusResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAdmin();

        // ── DELETE /settings/providers/{name} ───────────────────────────────────
        // Deletes a provider config file. Wikidata and local_filesystem cannot be deleted.

        grp.MapDelete("/providers/{name}", (
            string               name,
            IConfigurationLoader configLoader) =>
        {
            // Protect universe and filesystem providers.
            if (string.Equals(name, "wikidata", StringComparison.OrdinalIgnoreCase))
                return Results.Problem(
                    detail: "The Universe provider (Wikidata) cannot be removed. In a future version, this may be configurable.",
                    statusCode: StatusCodes.Status403Forbidden);

            if (string.Equals(name, "local_filesystem", StringComparison.OrdinalIgnoreCase))
                return Results.Problem(
                    detail: "The Local Filesystem provider cannot be removed.",
                    statusCode: StatusCodes.Status403Forbidden);

            var existing = configLoader.LoadProvider(name);
            if (existing is null)
                return Results.NotFound(new { error = $"Provider '{name}' not found." });

            // Disable rather than physically deleting the file — preserves history.
            existing.Enabled = false;
            configLoader.SaveProvider(existing);

            return Results.NoContent();
        })
        .WithName("DeleteProvider")
        .WithSummary("Removes a metadata provider (disables its configuration).")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAdmin();

        // ── PUT /settings/providers/priority ────────────────────────────────────
        // Saves the provider priority order.

        grp.MapPut("/providers/priority", (
            ProviderPriorityRequest request,
            IConfigurationLoader    configLoader) =>
        {
            if (request.Order is null || request.Order.Count == 0)
                return Results.BadRequest(new { error = "Order list cannot be empty." });

            var core = configLoader.LoadCore();
            core.ProviderPriority = request.Order;
            configLoader.SaveCore(core);

            return Results.Ok(new { order = request.Order });
        })
        .WithName("UpdateProviderPriority")
        .WithSummary("Saves the provider priority order for metadata harvesting.")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .RequireAdmin();

        // ── GET /settings/hydration ──────────────────────────────────────────
        grp.MapGet("/hydration", (IConfigurationLoader configLoader) =>
        {
            var settings = configLoader.LoadHydration();
            return Results.Ok(settings);
        })
        .WithName("GetHydrationSettings")
        .WithSummary("Load hydration pipeline configuration.")
        .Produces<HydrationSettings>(StatusCodes.Status200OK)
        .RequireAdminOrCurator();

        // ── PUT /settings/hydration ──────────────────────────────────────────
        grp.MapPut("/hydration", (
            HydrationSettings settings,
            IConfigurationLoader configLoader) =>
        {
            configLoader.SaveHydration(settings);
            return Results.Ok(new { saved = true });
        })
        .WithName("SaveHydrationSettings")
        .WithSummary("Save hydration pipeline configuration.")
        .Produces(StatusCodes.Status200OK)
        .RequireAdmin();

        // ── GET /settings/provider-slots ──────────────────────────────────────
        grp.MapGet("/provider-slots", (IConfigurationLoader configLoader) =>
        {
            var slots = configLoader.LoadSlots();
            return Results.Ok(slots.Slots);
        })
        .WithName("GetProviderSlots")
        .WithSummary("Load provider slot assignments per media type.")
        .Produces<Dictionary<string, ProviderSlotConfig>>(StatusCodes.Status200OK)
        .RequireAdminOrCurator();

        // ── PUT /settings/provider-slots ──────────────────────────────────────
        grp.MapPut("/provider-slots", (
            Dictionary<string, ProviderSlotConfig> slots,
            IConfigurationLoader configLoader) =>
        {
            if (slots is null || slots.Count == 0)
                return Results.BadRequest(new { error = "Slot assignments cannot be empty." });

            // Validate: no provider may occupy more than one slot per media type.
            foreach (var (mediaType, slot) in slots)
            {
                var assigned = new List<string>(3);
                if (!string.IsNullOrWhiteSpace(slot.Primary))   assigned.Add(slot.Primary);
                if (!string.IsNullOrWhiteSpace(slot.Secondary)) assigned.Add(slot.Secondary);
                if (!string.IsNullOrWhiteSpace(slot.Tertiary))  assigned.Add(slot.Tertiary);

                var duplicate = assigned
                    .GroupBy(n => n, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault(g => g.Count() > 1)?.Key;

                if (duplicate is not null)
                    return Results.BadRequest(new
                    {
                        error = $"Provider '{duplicate}' appears in multiple slots for '{mediaType}'. Each provider may only occupy one slot per media type."
                    });
            }

            var config = new ProviderSlotConfiguration { Slots = slots };
            configLoader.SaveSlots(config);

            return Results.Ok(new { saved = true });
        })
        .WithName("SaveProviderSlots")
        .WithSummary("Save provider slot assignments per media type.")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .RequireAdmin();

        // ── GET /settings/media-types ──────────────────────────────────────────
        grp.MapGet("/media-types", (IConfigurationLoader configLoader) =>
        {
            var config = configLoader.LoadMediaTypes();
            return Results.Ok(config);
        })
        .WithName("GetMediaTypes")
        .WithSummary("Load media type definitions including icons, extensions, and category folders.")
        .Produces<MediaTypeConfiguration>(StatusCodes.Status200OK)
        .RequireAdminOrCurator();

        // ── PUT /settings/media-types ──────────────────────────────────────────
        grp.MapPut("/media-types", (
            MediaTypeConfiguration config,
            IConfigurationLoader configLoader) =>
        {
            if (config?.Types is null || config.Types.Count == 0)
                return Results.BadRequest(new { error = "At least one media type is required." });

            var dupKeys = config.Types
                .GroupBy(t => t.Key, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(g => g.Count() > 1)?.Key;
            if (dupKeys is not null)
                return Results.BadRequest(new { error = $"Duplicate media type key: '{dupKeys}'." });

            var dupNames = config.Types
                .GroupBy(t => t.DisplayName, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(g => g.Count() > 1)?.Key;
            if (dupNames is not null)
                return Results.BadRequest(new { error = $"Duplicate media type display name: '{dupNames}'." });

            configLoader.SaveMediaTypes(config);
            return Results.Ok(new { saved = true });
        })
        .WithName("SaveMediaTypes")
        .WithSummary("Save media type definitions including custom types.")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .RequireAdmin();

        // ── POST /settings/media-types/add ────────────────────────────────────
        grp.MapPost("/media-types/add", (
            MediaTypeDefinition newType,
            IConfigurationLoader configLoader) =>
        {
            if (string.IsNullOrWhiteSpace(newType.Key) || string.IsNullOrWhiteSpace(newType.DisplayName))
                return Results.BadRequest(new { error = "Key and display name are required." });

            var config = configLoader.LoadMediaTypes();

            if (config.Types.Any(t => string.Equals(t.Key, newType.Key, StringComparison.OrdinalIgnoreCase)))
                return Results.BadRequest(new { error = $"Media type key '{newType.Key}' already exists." });

            if (config.Types.Any(t => string.Equals(t.DisplayName, newType.DisplayName, StringComparison.OrdinalIgnoreCase)))
                return Results.BadRequest(new { error = $"Media type '{newType.DisplayName}' already exists." });

            newType.BuiltIn = false;
            config.Types.Add(newType);
            configLoader.SaveMediaTypes(config);

            return Results.Ok(config);
        })
        .WithName("AddMediaType")
        .WithSummary("Add a custom media type definition.")
        .Produces<MediaTypeConfiguration>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .RequireAdmin();

        // ── DELETE /settings/media-types/{key} ────────────────────────────────
        grp.MapDelete("/media-types/{key}", (
            string key,
            IConfigurationLoader configLoader) =>
        {
            var config = configLoader.LoadMediaTypes();
            var existing = config.Types.FirstOrDefault(
                t => string.Equals(t.Key, key, StringComparison.OrdinalIgnoreCase));

            if (existing is null)
                return Results.NotFound(new { error = $"Media type '{key}' not found." });

            if (existing.BuiltIn)
                return Results.BadRequest(new { error = "Built-in media types cannot be deleted." });

            config.Types.Remove(existing);
            configLoader.SaveMediaTypes(config);

            // Clean up orphaned slot assignments for this media type.
            var slots = configLoader.LoadSlots();
            if (slots.Slots.Remove(existing.DisplayName))
                configLoader.SaveSlots(slots);

            return Results.NoContent();
        })
        .WithName("DeleteMediaType")
        .WithSummary("Remove a custom media type definition.")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAdmin();

        // ── Provider Icon Upload ──────────────────────────────────────────────

        grp.MapPost("/providers/{name}/icon", async (
            string name,
            HttpRequest request,
            IConfiguration config) =>
        {
            if (!request.HasFormContentType)
                return Results.BadRequest(new { error = "Expected multipart form data." });

            var form = await request.ReadFormAsync();
            var file = form.Files.FirstOrDefault();
            if (file is null || file.Length == 0)
                return Results.BadRequest(new { error = "No file uploaded." });

            if (file.Length > 256 * 1024)
                return Results.BadRequest(new { error = "Icon must be 256 KB or smaller." });

            var ext = Path.GetExtension(file.FileName)?.ToLowerInvariant();
            if (ext is not ".svg" and not ".png" and not ".jpg" and not ".jpeg")
                return Results.BadRequest(new { error = "Allowed formats: SVG, PNG, JPG." });

            var configDir = config["MediaEngine:ConfigDirectory"] ?? "config";
            var iconsDir  = Path.Combine(configDir, "icons");
            Directory.CreateDirectory(iconsDir);

            // Remove any existing icon for this provider.
            foreach (var existing in Directory.EnumerateFiles(iconsDir, $"{name}.*"))
                File.Delete(existing);

            var filePath = Path.Combine(iconsDir, $"{name}{ext}");
            await using var stream = File.Create(filePath);
            await file.CopyToAsync(stream);

            return Results.Ok(new { path = $"/settings/providers/{name}/icon" });
        })
        .WithName("UploadProviderIcon")
        .WithSummary("Upload an icon (SVG/PNG/JPG, max 256KB) for a provider.")
        .Accepts<IFormFile>("multipart/form-data")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .DisableAntiforgery()
        .RequireAdmin();

        grp.MapGet("/providers/{name}/icon", (
            string name,
            IConfiguration config) =>
        {
            var configDir = config["MediaEngine:ConfigDirectory"] ?? "config";
            var iconsDir  = Path.Combine(configDir, "icons");

            if (!Directory.Exists(iconsDir))
                return Results.NotFound();

            var match = Directory.EnumerateFiles(iconsDir, $"{name}.*").FirstOrDefault();
            if (match is null)
                return Results.NotFound();

            var ext = Path.GetExtension(match).ToLowerInvariant();
            var contentType = ext switch
            {
                ".svg"  => "image/svg+xml",
                ".png"  => "image/png",
                ".jpg"  => "image/jpeg",
                ".jpeg" => "image/jpeg",
                _       => "application/octet-stream",
            };

            return Results.File(match, contentType);
        })
        .WithName("GetProviderIcon")
        .WithSummary("Serve the uploaded icon for a provider.")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        // ── GET /settings/server-general ──────────────────────────────────────

        grp.MapGet("/server-general", (IConfigurationLoader configLoader) =>
        {
            var core = configLoader.LoadCore();
            return Results.Ok(new ServerGeneralResponse
            {
                ServerName  = core.ServerName,
                Language    = core.Language,
                Country     = core.Country,
                DateFormat  = core.DateFormat,
                TimeFormat  = core.TimeFormat,
            });
        })
        .WithName("GetServerGeneral")
        .WithSummary("Returns server identity and regional settings.")
        .Produces<ServerGeneralResponse>(StatusCodes.Status200OK)
        .RequireAdminOrCurator();

        // ── PUT /settings/server-general ──────────────────────────────────────

        grp.MapPut("/server-general", (
            ServerGeneralRequest request,
            IConfigurationLoader configLoader) =>
        {
            if (string.IsNullOrWhiteSpace(request.ServerName))
                return Results.BadRequest(new { error = "server_name cannot be empty" });

            var core = configLoader.LoadCore();
            core.ServerName = request.ServerName.Trim();
            core.Language   = request.Language;
            core.Country    = request.Country;
            core.DateFormat = request.DateFormat;
            core.TimeFormat = request.TimeFormat;
            configLoader.SaveCore(core);
            return Results.Ok();
        })
        .WithName("UpdateServerGeneral")
        .WithSummary("Saves server identity and regional settings.")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .RequireAdmin();

        return app;
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>Gets the primary base URL for a provider from its config endpoints.</summary>
    private static string? GetBaseUrlForProvider(ProviderConfiguration config)
    {
        // Convention: config-driven adapters use "api" as the primary endpoint key.
        if (config.Endpoints.TryGetValue("api", out var apiUrl) && !string.IsNullOrWhiteSpace(apiUrl))
            return apiUrl;

        // Try the endpoint key matching the provider name (legacy convention).
        if (config.Endpoints.TryGetValue(config.Name, out var url) && !string.IsNullOrWhiteSpace(url))
            return url;

        // Try well-known endpoint keys from the legacy mapping.
        if (_endpointKeys.TryGetValue(config.Name, out var epKey)
            && config.Endpoints.TryGetValue(epKey, out var ep)
            && !string.IsNullOrWhiteSpace(ep))
            return ep;

        // Fallback: return the first endpoint URL.
        return config.Endpoints.Values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
    }

    /// <summary>Resolve display name: prefer config's DisplayName, then fallback map, then raw name.</summary>
    private static string ResolveDisplayName(ProviderConfiguration config)
    {
        if (!string.IsNullOrWhiteSpace(config.DisplayName))
            return config.DisplayName;
        return _displayNames.TryGetValue(config.Name, out var dn) ? dn : config.Name;
    }

    /// <summary>Builds a <see cref="ProviderStatusResponse"/> from a provider config.</summary>
    private static ProviderStatusResponse BuildProviderStatusResponse(
        ProviderConfiguration provider,
        string displayName,
        bool isReachable = false)
    {
        // Prefer explicit can_handle.media_types; fall back to domain-derived media types.
        var mediaTypes = provider.CanHandle?.MediaTypes;
        if (mediaTypes is null || mediaTypes.Count == 0)
            mediaTypes = DeriveMediaTypesFromDomain(provider.Domain);

        return new ProviderStatusResponse
        {
            Name             = provider.Name,
            DisplayName      = displayName,
            Enabled          = provider.Enabled,
            IsZeroKey        = !provider.RequiresApiKey,
            IsReachable      = isReachable,
            Domain           = provider.Domain.ToString(),
            CapabilityTags   = provider.CapabilityTags,
            DefaultWeight    = provider.Weight,
            FieldWeights     = provider.FieldWeights,
            HydrationStages  = provider.HydrationStages,
            Endpoints        = provider.Endpoints,
            ThrottleMs       = provider.ThrottleMs,
            MaxConcurrency   = provider.MaxConcurrency,
            AvailableFields  = provider.AvailableFields,
            MediaTypes       = mediaTypes,
            RequiresApiKey   = provider.RequiresApiKey,
            HasApiKey        = !string.IsNullOrWhiteSpace(provider.HttpClient?.ApiKey),
            ApiKeyDelivery   = provider.HttpClient?.ApiKeyDelivery,
            ApiKeyParamName  = provider.HttpClient?.ApiKeyParamName,
            TimeoutSeconds   = provider.HttpClient?.TimeoutSeconds ?? 10,
            CustomIconName   = provider.CustomIconName,
            FieldMappings    = provider.FieldMappings?.Select(fm => new FieldMappingResponse
            {
                ClaimKey   = fm.ClaimKey,
                JsonPath   = fm.JsonPath,
                Confidence = fm.Confidence,
                Transform  = fm.Transform,
            }).ToList(),
        };
    }

    /// <summary>
    /// Derives media type capabilities from the provider's domain when
    /// <c>can_handle.media_types</c> is missing or empty in the config file.
    /// </summary>
    private static List<string> DeriveMediaTypesFromDomain(ProviderDomain domain) => domain switch
    {
        ProviderDomain.Ebook     => ["Books"],
        ProviderDomain.Audiobook => ["Audiobooks"],
        ProviderDomain.Comic     => ["Comic"],
        ProviderDomain.Video     => ["Movies", "TV"],
        ProviderDomain.Universal => ["Books", "Audiobooks", "Comic", "Movies", "TV"],
        _                        => [],
    };
}
