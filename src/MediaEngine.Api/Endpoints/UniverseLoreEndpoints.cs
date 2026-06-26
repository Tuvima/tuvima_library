using System.Text.Json.Serialization;
using MediaEngine.Api.Security;
using MediaEngine.Api.Services.Plugins;
using MediaEngine.Domain.Entities;

namespace MediaEngine.Api.Endpoints;

internal static class UniverseLoreEndpoints
{
    internal static RouteGroupBuilder MapUniverseLoreEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/universe/{qid}")
            .WithTags("Universe Lore");

        group.MapGet("/lore-sources", async (
            string qid,
            PluginUniverseLoreService lore,
            CancellationToken ct) =>
        {
            var sources = await lore.GetSourcesAsync(qid, ct).ConfigureAwait(false);
            return Results.Ok(sources.Select(ToDto).ToList());
        })
        .WithName("GetUniverseLoreSources")
        .RequireAdmin();

        group.MapPost("/lore-sources/discover", async (
            string qid,
            PluginUniverseLoreService lore,
            CancellationToken ct) =>
        {
            var sources = await lore.DiscoverSourcesAsync(qid, ct).ConfigureAwait(false);
            return Results.Ok(sources.Select(ToDto).ToList());
        })
        .WithName("DiscoverUniverseLoreSources")
        .RequireAdmin();

        group.MapPost("/lore-sources/manual", async (
            string qid,
            ManualLoreSourceRequest request,
            PluginUniverseLoreService lore,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.BaseUrl))
                return Results.BadRequest("A base URL is required.");

            try
            {
                var source = await lore.AddManualSourceAsync(
                    qid,
                    request.SourceName ?? string.Empty,
                    request.BaseUrl,
                    request.ApiUrl,
                    ct).ConfigureAwait(false);
                return Results.Ok(ToDto(source));
            }
            catch (UriFormatException ex)
            {
                return Results.BadRequest(ex.Message);
            }
        })
        .WithName("AddManualUniverseLoreSource")
        .RequireAdmin();

        group.MapPost("/lore-sources/{sourceId:guid}/approve", async (
            string qid,
            Guid sourceId,
            PluginUniverseLoreService lore,
            CancellationToken ct) =>
        {
            try
            {
                var sources = await lore.SetSourceStatusAsync(
                    qid,
                    sourceId,
                    PluginLoreSourceStatus.Approved,
                    actor: "admin",
                    ct).ConfigureAwait(false);
                return Results.Ok(sources.Select(ToDto).ToList());
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(ex.Message);
            }
        })
        .WithName("ApproveUniverseLoreSource")
        .RequireAdmin();

        group.MapPost("/lore-sources/{sourceId:guid}/reject", async (
            string qid,
            Guid sourceId,
            PluginUniverseLoreService lore,
            CancellationToken ct) =>
        {
            try
            {
                var sources = await lore.SetSourceStatusAsync(
                    qid,
                    sourceId,
                    PluginLoreSourceStatus.Rejected,
                    actor: "admin",
                    ct).ConfigureAwait(false);
                return Results.Ok(sources.Select(ToDto).ToList());
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(ex.Message);
            }
        })
        .WithName("RejectUniverseLoreSource")
        .RequireAdmin();

        group.MapPost("/lore/enrich", async (
            string qid,
            PluginUniverseLoreService lore,
            CancellationToken ct) =>
        {
            var summary = await lore.EnrichUniverseAsync(qid, ct).ConfigureAwait(false);
            return Results.Ok(new
            {
                universe_qid = summary.UniverseQid,
                sources_enriched = summary.SourcesEnriched,
                entities_written = summary.EntitiesWritten,
                relationships_written = summary.RelationshipsWritten,
            });
        })
        .WithName("EnrichUniverseLoreSources")
        .RequireAdmin();

        return group;
    }

    private static object ToDto(PluginLoreSourceRecord source) => new
    {
        id = source.Id,
        universe_qid = source.UniverseQid,
        plugin_id = source.PluginId,
        source_key = source.SourceKey,
        source_name = source.SourceName,
        base_url = source.BaseUrl,
        api_url = source.ApiUrl,
        status = source.Status,
        confidence = source.Confidence,
        license = source.License,
        approved_at = source.ApprovedAt,
        approved_by = source.ApprovedBy,
        rejected_at = source.RejectedAt,
        last_discovered_at = source.LastDiscoveredAt,
        last_enriched_at = source.LastEnrichedAt,
        created_at = source.CreatedAt,
        updated_at = source.UpdatedAt,
    };
}

internal sealed record ManualLoreSourceRequest(
    [property: JsonPropertyName("source_name")] string? SourceName,
    [property: JsonPropertyName("base_url")] string BaseUrl,
    [property: JsonPropertyName("api_url")] string? ApiUrl);
