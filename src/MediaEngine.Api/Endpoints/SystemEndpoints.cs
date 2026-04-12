using System.Reflection;
using MediaEngine.Api.Models;
using MediaEngine.Api.Security;
using MediaEngine.Domain.Services;
using MediaEngine.Ingestion.Contracts;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Api.Endpoints;

public static class SystemEndpoints
{
    // Version sourced from the assembly at startup — no hard-coded string to forget to bump.
    private static readonly string AppVersion =
        typeof(SystemEndpoints).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
            ?.Split('+')[0]           // strip build metadata (e.g. git hash)
        ?? "1.0.0";

    public static IEndpointRouteBuilder MapSystemEndpoints(this IEndpointRouteBuilder app)
    {
        // No auth required — allows external apps to verify the URL is reachable.
        // The X-Api-Key middleware validates the key if one is supplied, returning
        // 401 for invalid keys; absent keys pass through to this endpoint.
        app.MapGet("/system/status", (IConfigurationLoader configLoader) =>
        {
            var core = configLoader.LoadCore();
            return Results.Ok(new SystemStatusResponse
            {
                Status   = "ok",
                Version  = AppVersion,
                Language = core?.Language.Metadata ?? "en",
            });
        })
        .WithTags("System")
        .WithName("GetSystemStatus")
        .WithSummary("Returns service health and version. Used by external apps to test connectivity.")
        .Produces<SystemStatusResponse>(StatusCodes.Status200OK);

        app.MapGet("/system/watcher-status", (IFileWatcher watcher) =>
            Results.Ok(new
            {
                running         = watcher.IsRunning,
                directory_count = watcher.WatchedPaths.Count,
                directories     = watcher.WatchedPaths,
                event_count     = watcher.EventCount,
                last_event_at   = watcher.LastEventAt,
            }))
        .WithTags("System")
        .WithName("GetWatcherStatus")
        .WithSummary("Returns file watcher diagnostic status.")
        .RequireAdmin();

        // ── POST /maintenance/sweep-orphan-images ─────────────────────────────
        // Scans the .images/ directory tree for subdirectories whose QID (or
        // provisional GUID prefix) no longer exists in the database.  Safe to run
        // at any time — best-effort, never throws on individual directory failures.
        app.MapPost("/maintenance/sweep-orphan-images", async (
            ImagePathService imagePaths,
            IDatabaseConnection db,
            CancellationToken ct) =>
        {
            var imagesRoot = imagePaths.ImagesRoot;
            if (!Directory.Exists(imagesRoot))
                return Results.Ok(new { cleaned = 0, message = ".images/ directory does not exist — nothing to sweep." });

            int cleaned = 0;

            // ── Collect known QIDs and work IDs from the database ─────────────
            var knownWorkQids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var knownWorkId12 = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var knownPersonQids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var knownUniverseQids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                using var conn = db.CreateConnection();

                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT DISTINCT wikidata_qid FROM works WHERE wikidata_qid IS NOT NULL";
                    using var r = cmd.ExecuteReader();
                    while (r.Read()) knownWorkQids.Add(r.GetString(0));
                }

                using (var cmd = conn.CreateCommand())
                {
                    // Provisional slot uses first 12 hex chars of any media_asset id linked to the work
                    cmd.CommandText = """
                        SELECT DISTINCT LOWER(SUBSTR(REPLACE(ma.id, '-', ''), 1, 12))
                        FROM media_assets ma
                        INNER JOIN editions e ON e.id = ma.edition_id
                        INNER JOIN works w ON w.id = e.work_id
                        WHERE w.wikidata_qid IS NULL
                        """;
                    using var r = cmd.ExecuteReader();
                    while (r.Read()) knownWorkId12.Add(r.GetString(0));
                }

                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT DISTINCT wikidata_qid FROM persons WHERE wikidata_qid IS NOT NULL";
                    using var r = cmd.ExecuteReader();
                    while (r.Read()) knownPersonQids.Add(r.GetString(0));
                }

                // Universe images use the QID of the parent collection (franchise-level collection)
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = """
                        SELECT DISTINCT wikidata_qid FROM collections
                        WHERE wikidata_qid IS NOT NULL AND parent_collection_id IS NOT NULL
                        UNION
                        SELECT DISTINCT wikidata_qid FROM collections
                        WHERE wikidata_qid IS NOT NULL AND id IN (
                            SELECT DISTINCT parent_collection_id FROM collections WHERE parent_collection_id IS NOT NULL
                        )
                        """;
                    using var r = cmd.ExecuteReader();
                    while (r.Read()) knownUniverseQids.Add(r.GetString(0));
                }
            }
            catch (Exception ex)
            {
                return Results.Problem($"Failed to query database: {ex.Message}");
            }

            ct.ThrowIfCancellationRequested();

            // ── Sweep .images/works/{QID}/ ────────────────────────────────────
            var worksDir = Path.Combine(imagesRoot, "works");
            if (Directory.Exists(worksDir))
            {
                foreach (var dir in Directory.EnumerateDirectories(worksDir))
                {
                    ct.ThrowIfCancellationRequested();
                    var name = Path.GetFileName(dir);
                    if (string.Equals(name, "_pending", StringComparison.OrdinalIgnoreCase))
                        continue; // handled separately below

                    if (!knownWorkQids.Contains(name))
                    {
                        try { Directory.Delete(dir, recursive: true); cleaned++; }
                        catch { /* best-effort */ }
                    }
                }

                // ── Sweep .images/works/_pending/{id12}/ ─────────────────────
                var provisionalDir = Path.Combine(worksDir, "_pending");
                if (Directory.Exists(provisionalDir))
                {
                    foreach (var dir in Directory.EnumerateDirectories(provisionalDir))
                    {
                        ct.ThrowIfCancellationRequested();
                        var name = Path.GetFileName(dir);
                        if (!knownWorkId12.Contains(name))
                        {
                            try { Directory.Delete(dir, recursive: true); cleaned++; }
                            catch { /* best-effort */ }
                        }
                    }
                }
            }

            // ── Sweep .images/people/{QID}/ ───────────────────────────────────
            var peopleDir = Path.Combine(imagesRoot, "people");
            if (Directory.Exists(peopleDir))
            {
                foreach (var dir in Directory.EnumerateDirectories(peopleDir))
                {
                    ct.ThrowIfCancellationRequested();
                    var name = Path.GetFileName(dir);
                    if (!knownPersonQids.Contains(name))
                    {
                        try { Directory.Delete(dir, recursive: true); cleaned++; }
                        catch { /* best-effort */ }
                    }
                }
            }

            // ── Sweep .images/universes/{QID}/ ────────────────────────────────
            var universesDir = Path.Combine(imagesRoot, "universes");
            if (Directory.Exists(universesDir))
            {
                foreach (var dir in Directory.EnumerateDirectories(universesDir))
                {
                    ct.ThrowIfCancellationRequested();
                    var name = Path.GetFileName(dir);
                    if (!knownUniverseQids.Contains(name))
                    {
                        try { Directory.Delete(dir, recursive: true); cleaned++; }
                        catch { /* best-effort */ }
                    }
                }
            }

            return Results.Ok(new
            {
                cleaned,
                message = cleaned == 0
                    ? "No orphaned image directories found."
                    : $"Removed {cleaned} orphaned image director{(cleaned == 1 ? "y" : "ies")}.",
            });
        })
        .WithTags("System")
        .WithName("SweepOrphanImages")
        .WithSummary("Scans .images/ for directories with no matching database entity and removes them.")
        .Produces(StatusCodes.Status200OK)
        .RequireAdmin();

        return app;
    }
}
