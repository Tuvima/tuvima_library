using System.Text.Json;
using MediaEngine.Api.Http;
using MediaEngine.Api.Security;
using MediaEngine.Api.Services.Plugins;
using MediaEngine.Contracts.Plugins;
using MediaEngine.Domain.Contracts;
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
            .Produces<IEnumerable<PluginSummaryResponse>>(StatusCodes.Status200OK)
            .RequireAdmin();

        group.MapGet("/approved", async (ApprovedPluginCatalogService catalog, CancellationToken ct) =>
            Results.Ok(await catalog.GetAsync(ct).ConfigureAwait(false)))
            .WithName("ListApprovedPlugins")
            .Produces<ApprovedPluginCatalogDto>(StatusCodes.Status200OK)
            .RequireAdmin();

        group.MapGet("/{pluginId}", (string pluginId, PluginCatalog catalog) =>
        {
            var plugin = catalog.Get(pluginId);
            return plugin is null ? ApiErrors.NotFound($"Plugin '{pluginId}' not found.") : Results.Ok(ToDto(plugin));
        })
        .WithName("GetPlugin")
        .Produces<PluginSummaryResponse>(StatusCodes.Status200OK)
        .RequireAdmin();

        group.MapPost("/{pluginId}/enable", (string pluginId, PluginCatalog catalog) =>
        {
            catalog.SetEnabled(pluginId, true);
            return Results.Ok(new PluginEnabledResponse { plugin_id = pluginId, enabled = true });
        })
        .WithName("EnablePlugin")
        .Produces<PluginEnabledResponse>(StatusCodes.Status200OK)
        .RequireAdmin();

        group.MapPost("/{pluginId}/disable", (string pluginId, PluginCatalog catalog) =>
        {
            catalog.SetEnabled(pluginId, false);
            return Results.Ok(new PluginEnabledResponse { plugin_id = pluginId, enabled = false });
        })
        .WithName("DisablePlugin")
        .Produces<PluginEnabledResponse>(StatusCodes.Status200OK)
        .RequireAdmin();

        group.MapPut("/{pluginId}/settings", (
            string pluginId,
            Dictionary<string, JsonElement> settings,
            PluginCatalog catalog) =>
        {
            catalog.SaveSettings(pluginId, settings);
            return Results.Ok(new PluginSavedResponse { plugin_id = pluginId, saved = true });
        })
        .WithName("SavePluginSettings")
        .Produces<PluginSavedResponse>(StatusCodes.Status200OK)
        .RequireAdmin();

        group.MapGet("/{pluginId}/manifest", (string pluginId, PluginCatalog catalog) =>
        {
            try
            {
                return Results.Ok(new PluginManifestJsonResponse { plugin_id = pluginId, json = catalog.GetManifestJson(pluginId) });
            }
            catch (InvalidOperationException ex)
            {
                return ApiErrors.BadRequest(ex.Message);
            }
        })
        .WithName("GetPluginManifestJson")
        .Produces<PluginManifestJsonResponse>(StatusCodes.Status200OK)
        .RequireAdmin();

        group.MapPut("/{pluginId}/manifest", (
            string pluginId,
            PluginJsonUpdateRequest request,
            PluginCatalog catalog) =>
        {
            try
            {
                catalog.SaveManifestJson(pluginId, request.Json);
                return Results.Ok(new PluginSavedResponse { plugin_id = pluginId, saved = true });
            }
            catch (JsonException ex)
            {
                return ApiErrors.BadRequest($"Plugin manifest JSON is invalid: {ex.Message}");
            }
            catch (InvalidOperationException ex)
            {
                return ApiErrors.BadRequest(ex.Message);
            }
        })
        .WithName("SavePluginManifestJson")
        .Produces<PluginSavedResponse>(StatusCodes.Status200OK)
        .RequireAdmin();

        group.MapDelete("/{pluginId}", (string pluginId, PluginCatalog catalog) =>
        {
            try
            {
                catalog.DeletePlugin(pluginId);
                return Results.Ok(new PluginDeletedResponse { plugin_id = pluginId, deleted = true });
            }
            catch (InvalidOperationException ex)
            {
                return ApiErrors.BadRequest(ex.Message);
            }
        })
        .WithName("DeletePlugin")
        .Produces<PluginDeletedResponse>(StatusCodes.Status200OK)
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
                return ApiErrors.NotFound($"Plugin '{pluginId}' not found.");

            var temp = Path.Combine(Path.GetTempPath(), "tuvima-plugins", plugin.Manifest.Id, "health");
            Directory.CreateDirectory(temp);
            var context = new PluginExecutionContext(plugin.Manifest.Id, plugin.Settings, temp, tools, ai);
            var checks = new List<PluginHealthResult>();
            foreach (var check in plugin.Capabilities.OfType<IPluginHealthCheck>())
                checks.Add(await check.GetHealthAsync(context, ct).ConfigureAwait(false));

            return Results.Ok(new PluginHealthCheckResponse
            {
                plugin_id = plugin.Manifest.Id,
                status = checks.Any(c => c.Status == "degraded") ? "degraded" : checks.Count == 0 ? "unknown" : "healthy",
                checks = checks,
            });
        })
        .WithName("CheckPluginHealth")
        .Produces<PluginHealthCheckResponse>(StatusCodes.Status200OK)
        .RequireAdmin();

        group.MapGet("/{pluginId}/jobs", async (
            string pluginId,
            IMediaOperationRepository operations,
            CancellationToken ct) =>
        {
            var jobs = await operations.GetByPluginAsync(pluginId, 200, ct).ConfigureAwait(false);
            return Results.Ok(jobs.Select((op, index) => OperationDto.From(op, index + 1)).ToList());
        })
            .WithName("GetPluginJobs")
            .Produces<IReadOnlyList<OperationDto>>(StatusCodes.Status200OK)
            .RequireAdmin();

        group.MapPost("/jobs/segment-detection/run", async (
            PluginScheduledSegmentService scheduler,
            CancellationToken ct) =>
        {
            var jobs = await scheduler.RunScheduledPassAsync(ct).ConfigureAwait(false);
            return Results.Ok(jobs);
        })
        .WithName("RunPluginSegmentDetectionJobs")
        .Produces<IReadOnlyList<PluginJobSnapshot>>(StatusCodes.Status200OK)
        .RequireAdmin();

        return group;
    }

    private static PluginSummaryResponse ToDto(PluginRegistration registration) => new(
        registration.Manifest.Id,
        registration.Manifest.Name,
        registration.Manifest.Version,
        registration.Manifest.Description,
        registration.Enabled,
        registration.IsBuiltIn,
        registration.LoadError,
        registration.Manifest.Capabilities,
        registration.Manifest.Permissions,
        registration.Manifest.ToolRequirements,
        registration.Manifest.AiPermissions,
        registration.Settings,
        registration.SettingsSchema,
        registration.ManifestPath);
}

internal sealed record PluginJsonUpdateRequest(string Json);

/// <summary>
/// Response for <c>POST /plugins/{pluginId}/health</c>. Declared here rather than in
/// <c>MediaEngine.Contracts</c> because <see cref="PluginHealthResult"/> lives in
/// <c>MediaEngine.Plugins</c>, and Contracts may only reference Domain.
/// </summary>
internal sealed record PluginHealthCheckResponse
{
    public string plugin_id { get; init; } = string.Empty;
    public string status { get; init; } = string.Empty;
    public List<PluginHealthResult> checks { get; init; } = [];
}
