using MediaEngine.Domain.Models;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Api.Endpoints;

/// <summary>
/// Provider catalogue endpoint — serves consolidated provider UI metadata
/// from each provider's config JSON file.
///
/// <para>
/// This centralises display names, accent colours, material icons, external URL
/// templates, category labels, auth types, and per-media-type search/ranking
/// chip labels that were previously hardcoded across ~15 Dashboard files.
/// The Dashboard reads this endpoint once on load and caches the result.
/// </para>
///
/// Access: anonymous (no sensitive data; purely display metadata).
/// Route:  <c>GET /providers/catalogue</c>
/// </summary>
public static class ProviderCatalogueEndpoints
{
    public static IEndpointRouteBuilder MapProviderCatalogueEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/providers").WithTags("Providers");

        // ── GET /providers/catalogue ─────────────────────────────────────────────

        grp.MapGet("/catalogue", (IConfigurationLoader configLoader) =>
        {
            var providers = configLoader.LoadAllProviders();
            var catalogue = providers
                .Where(p => p.ProviderId is not null)   // coded adapters without GUIDs are internal only
                .Select(p => MapToEntry(p))
                .ToList();

            return Results.Ok(catalogue);
        })
        .WithName("GetProviderCatalogue")
        .WithSummary("Returns consolidated UI metadata for all configured providers.")
        .AllowAnonymous();

        return app;
    }

    // ── Private helpers ──────────────────────────────────────────────────────────

    private static ProviderCatalogueEntry MapToEntry(MediaEngine.Storage.Models.ProviderConfiguration p)
    {
        var ui = p.UiMetadata;

        // Build per-media-type search/ranking chip dictionaries
        var searchChips  = BuildChips(ui?.SearchChips);
        var rankingChips = BuildChips(ui?.RankingChips);

        // Fall back to display_name → formatted name string
        var displayName = p.DisplayName ?? FormatProviderName(p.Name);

        return new ProviderCatalogueEntry
        {
            ProviderId          = p.ProviderId!,
            Name                = p.Name,
            DisplayName         = displayName,
            Enabled             = p.Enabled,
            Domain              = p.Domain.ToString(),
            MediaTypes          = p.CanHandle?.MediaTypes ?? [],
            AccentColor         = ui?.AccentColor ?? "#90A4AE",
            MaterialIcon        = ui?.MaterialIcon ?? p.CustomIconName ?? "Cloud",
            ExternalUrlTemplate = string.IsNullOrEmpty(ui?.ExternalUrlTemplate) ? null : ui.ExternalUrlTemplate,
            Category            = ui?.Category ?? "Open",
            RequiresKey         = ui?.RequiresKey ?? p.RequiresApiKey,
            AuthType            = ui?.AuthType ?? ResolveAuthType(p),
            SearchChips         = searchChips,
            RankingChips        = rankingChips,
            IconPath            = p.Icon,
            HydrationStages     = [.. p.HydrationStages],
            LanguageStrategy    = p.LanguageStrategyRaw,
        };
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> BuildChips(
        Dictionary<string, List<string>>? source)
    {
        if (source is null or { Count: 0 })
            return new Dictionary<string, IReadOnlyList<string>>();

        return source.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyList<string>)kv.Value.AsReadOnly());
    }

    private static string ResolveAuthType(MediaEngine.Storage.Models.ProviderConfiguration p)
    {
        var delivery = p.HttpClient?.ApiKeyDelivery?.ToLowerInvariant();
        return delivery switch
        {
            "bearer" => "bearer",
            "basic"  => "basic",
            "query"  => "api_key",
            "header" => "api_key",
            _        => "none",
        };
    }

    private static string FormatProviderName(string key) =>
        string.Join(' ', key.Split('_')
            .Select(w => w.Length > 0 ? char.ToUpperInvariant(w[0]) + w[1..] : w));
}
