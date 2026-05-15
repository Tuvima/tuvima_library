using System.Text.Json;
using MediaEngine.Api.Security;
using MediaEngine.Api.Services.Plugins;
using MediaEngine.Plugins;

namespace MediaEngine.Api.Endpoints;

internal static class PluginEndpoints
{
    internal static RouteGroupBuilder MapPluginEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/plugins")
            .WithTags("Plugins");

        group.MapGet("", (PluginCatalog catalog) =>
            Results.Ok(catalog.List().Select(ToDto)))
            .WithName("ListPlugins")
            .RequireAdmin();

        group.MapGet("/approved", async (ApprovedPluginCatalogService catalog, CancellationToken ct) =>
            Results.Ok(await catalog.GetAsync(ct).ConfigureAwait(false)))
            .WithName("ListApprovedPlugins")
            .RequireAdmin();

        group.MapGet("/{pluginId}", (string pluginId, PluginCatalog catalog) =>
        {
            var plugin = catalog.Get(pluginId);
            return plugin is null ? Results.NotFound($"Plugin '{pluginId}' not found.") : Results.Ok(ToDto(plugin));
        })
        .WithName("GetPlugin")
        .RequireAdmin();

        group.MapPost("/{pluginId}/enable", (string pluginId, PluginCatalog catalog) =>
        {
            catalog.SetEnabled(pluginId, true);
            return Results.Ok(new { plugin_id = pluginId, enabled = true });
        })
        .WithName("EnablePlugin")
        .RequireAdmin();

        group.MapPost("/{pluginId}/disable", (string pluginId, PluginCatalog catalog) =>
        {
            catalog.SetEnabled(pluginId, false);
            return Results.Ok(new { plugin_id = pluginId, enabled = false });
        })
        .WithName("DisablePlugin")
        .RequireAdmin();

        group.MapPut("/{pluginId}/settings", (
            string pluginId,
            Dictionary<string, JsonElement> settings,
            PluginCatalog catalog) =>
        {
            catalog.SaveSettings(pluginId, settings);
            return Results.Ok(new { plugin_id = pluginId, saved = true });
        })
        .WithName("SavePluginSettings")
        .RequireAdmin();

        group.MapGet("/{pluginId}/manifest", (string pluginId, PluginCatalog catalog) =>
        {
            try
            {
                return Results.Ok(new { plugin_id = pluginId, json = catalog.GetManifestJson(pluginId) });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(ex.Message);
            }
        })
        .WithName("GetPluginManifestJson")
        .RequireAdmin();

        group.MapPut("/{pluginId}/manifest", (
            string pluginId,
            PluginJsonUpdateRequest request,
            PluginCatalog catalog) =>
        {
            try
            {
                catalog.SaveManifestJson(pluginId, request.Json);
                return Results.Ok(new { plugin_id = pluginId, saved = true });
            }
            catch (JsonException ex)
            {
                return Results.BadRequest($"Plugin manifest JSON is invalid: {ex.Message}");
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(ex.Message);
            }
        })
        .WithName("SavePluginManifestJson")
        .RequireAdmin();

        group.MapDelete("/{pluginId}", (string pluginId, PluginCatalog catalog) =>
        {
            try
            {
                catalog.DeletePlugin(pluginId);
                return Results.Ok(new { plugin_id = pluginId, deleted = true });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(ex.Message);
            }
        })
        .WithName("DeletePlugin")
        .RequireAdmin();

        group.MapPost("/{pluginId}/health", async (
            string pluginId,
            PluginCatalog catalog,
            IPluginToolRuntime tools,
            IPluginAiClient ai,
            CancellationToken ct) =>
        {
            var plugin = catalog.Get(pluginId);
            if (plugin is null)
                return Results.NotFound($"Plugin '{pluginId}' not found.");

            var temp = Path.Combine(Path.GetTempPath(), "tuvima-plugins", plugin.Manifest.Id, "health");
            Directory.CreateDirectory(temp);
            var context = new PluginExecutionContext(plugin.Manifest.Id, plugin.Settings, temp, tools, ai);
            var checks = new List<PluginHealthResult>();
            foreach (var check in plugin.Capabilities.OfType<IPluginHealthCheck>())
                checks.Add(await check.GetHealthAsync(context, ct).ConfigureAwait(false));

            return Results.Ok(new
            {
                plugin_id = plugin.Manifest.Id,
                status = checks.Any(c => c.Status == "degraded") ? "degraded" : checks.Count == 0 ? "unknown" : "healthy",
                checks,
            });
        })
        .WithName("CheckPluginHealth")
        .RequireAdmin();

        group.MapGet("/{pluginId}/jobs", (string pluginId, PluginJobStateService jobs) =>
            Results.Ok(jobs.List(pluginId)))
            .WithName("GetPluginJobs")
            .RequireAdmin();

        group.MapPost("/jobs/segment-detection/run", async (
            PluginScheduledSegmentService scheduler,
            CancellationToken ct) =>
        {
            var jobs = await scheduler.RunScheduledPassAsync(ct).ConfigureAwait(false);
            return Results.Ok(jobs);
        })
        .WithName("RunPluginSegmentDetectionJobs")
        .RequireAdmin();

        return group;
    }

    private static object ToDto(PluginRegistration registration) => new
    {
        id = registration.Manifest.Id,
        name = registration.Manifest.Name,
        version = registration.Manifest.Version,
        description = registration.Manifest.Description,
        enabled = registration.Enabled,
        is_built_in = registration.IsBuiltIn,
        load_error = registration.LoadError,
        capabilities = registration.Manifest.Capabilities,
        permissions = registration.Manifest.Permissions,
        tool_requirements = registration.Manifest.ToolRequirements,
        ai_permissions = registration.Manifest.AiPermissions,
        settings = registration.Settings,
        manifest_path = registration.ManifestPath,
    };
}

internal sealed record PluginJsonUpdateRequest(string Json);

