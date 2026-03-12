using System.Text.Json;
using System.Text.Json.Serialization;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Storage.Contracts;
using Microsoft.Data.Sqlite;

namespace MediaEngine.Api.Endpoints;

public static class ProgressEndpoints
{
    public static IEndpointRouteBuilder MapProgressEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/progress")
                       .WithTags("Progress");

        // GET /progress/{assetId}?userId={userId} — current progress for an asset.
        group.MapGet("/{assetId:guid}", async (
            Guid assetId,
            string? userId,
            IUserStateStore stateStore,
            CancellationToken ct) =>
        {
            var uid = ResolveUserId(userId);
            var state = await stateStore.GetAsync(uid, assetId, ct);
            return state is null
                ? Results.NotFound("No progress recorded for this asset.")
                : Results.Ok(MapStateResponse(state));
        });

        // PUT /progress/{assetId} — upsert progress for an asset.
        group.MapPut("/{assetId:guid}", async (
            Guid assetId,
            ProgressUpdateRequest body,
            IUserStateStore stateStore,
            IMediaAssetRepository assetRepo,
            CancellationToken ct) =>
        {
            var asset = await assetRepo.FindByIdAsync(assetId, ct);
            if (asset is null)
                return Results.NotFound($"Asset '{assetId}' not found.");

            var state = new UserState
            {
                UserId       = ResolveUserId(body.UserId),
                AssetId      = assetId,
                ContentHash  = asset.ContentHash,
                ProgressPct  = Math.Clamp(body.ProgressPct, 0.0, 100.0),
                LastAccessed = DateTimeOffset.UtcNow,
                ExtendedProperties = body.ExtendedProperties ?? [],
            };

            await stateStore.SaveAsync(state, ct);
            return Results.Ok(MapStateResponse(state));
        });

        // GET /progress/recent?userId={userId}&limit=10 — recently accessed items.
        group.MapGet("/recent", async (
            string? userId,
            int? limit,
            IUserStateStore stateStore,
            CancellationToken ct) =>
        {
            var uid = ResolveUserId(userId);
            var items = await stateStore.GetRecentAsync(uid, limit ?? 10, ct);
            return Results.Ok(items.Select(MapStateResponse));
        });

        // GET /progress/journey?userId={userId}&hubId={hubId}&limit=5 — incomplete items with
        // Work+Hub context for "Continue your Journey" hero.
        // Optional hubId: when supplied, results are filtered to assets that belong to that
        // hub via works.hub_id, eliminating any client-side matching ambiguity.
        group.MapGet("/journey", async (
            string? userId,
            string? hubId,
            int? limit,
            IDatabaseConnection db,
            CancellationToken ct) =>
        {
            ct.ThrowIfCancellationRequested();
            var uid       = ResolveUserId(userId);
            var filterHub = Guid.TryParse(hubId, out var parsedHubId);
            var conn      = db.Open();

            // Canonical values may be stored at the work level OR the asset level
            // depending on which scoring pass ran. We COALESCE work-level first, then
            // asset-level as a fallback, so either storage pattern returns the right data.
            const string baseSelect = """
                SELECT
                    us.asset_id,
                    w.id            AS work_id,
                    w.hub_id,
                    w.media_type,
                    us.progress_pct,
                    us.last_accessed,
                    us.extended_properties,
                    h.display_name  AS hub_display_name,
                    COALESCE(cv_title_w.value,       cv_title_a.value)       AS title,
                    COALESCE(cv_author_w.value,      cv_author_a.value)      AS author,
                    COALESCE(cv_cover_w.value,       cv_cover_a.value)       AS cover_url,
                    COALESCE(cv_narrator_w.value,    cv_narrator_a.value)    AS narrator,
                    COALESCE(cv_series_w.value,      cv_series_a.value)      AS series,
                    COALESCE(cv_series_pos_w.value,  cv_series_pos_a.value)  AS series_position,
                    COALESCE(cv_desc_w.value,        cv_desc_a.value)        AS description
                FROM user_states us
                JOIN media_assets ma ON ma.id = us.asset_id
                JOIN editions e      ON e.id  = ma.edition_id
                JOIN works w         ON w.id  = e.work_id
                LEFT JOIN hubs h     ON h.id  = w.hub_id
                LEFT JOIN canonical_values cv_title_w
                    ON cv_title_w.entity_id = w.id        AND cv_title_w.key = 'title'
                LEFT JOIN canonical_values cv_title_a
                    ON cv_title_a.entity_id = ma.id       AND cv_title_a.key = 'title'
                LEFT JOIN canonical_values cv_author_w
                    ON cv_author_w.entity_id = w.id       AND cv_author_w.key = 'author'
                LEFT JOIN canonical_values cv_author_a
                    ON cv_author_a.entity_id = ma.id      AND cv_author_a.key = 'author'
                LEFT JOIN canonical_values cv_cover_w
                    ON cv_cover_w.entity_id = w.id        AND cv_cover_w.key = 'cover'
                LEFT JOIN canonical_values cv_cover_a
                    ON cv_cover_a.entity_id = ma.id       AND cv_cover_a.key = 'cover'
                LEFT JOIN canonical_values cv_narrator_w
                    ON cv_narrator_w.entity_id = w.id     AND cv_narrator_w.key = 'narrator'
                LEFT JOIN canonical_values cv_narrator_a
                    ON cv_narrator_a.entity_id = ma.id    AND cv_narrator_a.key = 'narrator'
                LEFT JOIN canonical_values cv_series_w
                    ON cv_series_w.entity_id = w.id       AND cv_series_w.key = 'series'
                LEFT JOIN canonical_values cv_series_a
                    ON cv_series_a.entity_id = ma.id      AND cv_series_a.key = 'series'
                LEFT JOIN canonical_values cv_series_pos_w
                    ON cv_series_pos_w.entity_id = w.id   AND cv_series_pos_w.key = 'series_position'
                LEFT JOIN canonical_values cv_series_pos_a
                    ON cv_series_pos_a.entity_id = ma.id  AND cv_series_pos_a.key = 'series_position'
                LEFT JOIN canonical_values cv_desc_w
                    ON cv_desc_w.entity_id = w.id         AND cv_desc_w.key = 'description'
                LEFT JOIN canonical_values cv_desc_a
                    ON cv_desc_a.entity_id = ma.id        AND cv_desc_a.key = 'description'
                WHERE us.user_id = @userId
                  AND us.progress_pct > 0.0
                  AND us.progress_pct < 100.0
                """;

            using var cmd = conn.CreateCommand();
            cmd.CommandText = filterHub
                ? baseSelect + "\n  AND w.hub_id = @hubId\nORDER BY us.last_accessed DESC\nLIMIT @limit;"
                : baseSelect + "\nORDER BY us.last_accessed DESC\nLIMIT @limit;";

            cmd.Parameters.AddWithValue("@userId", uid.ToString());
            cmd.Parameters.AddWithValue("@limit", limit ?? 5);
            if (filterHub)
                cmd.Parameters.AddWithValue("@hubId", parsedHubId.ToString());

            using var reader = cmd.ExecuteReader();
            var results = new List<JourneyItemResponse>();
            while (reader.Read())
            {
                var extJson = reader.IsDBNull(6) ? null : reader.GetString(6);
                var extProps = string.IsNullOrEmpty(extJson)
                    ? new Dictionary<string, string>()
                    : JsonSerializer.Deserialize<Dictionary<string, string>>(extJson)
                      ?? new Dictionary<string, string>();

                results.Add(new JourneyItemResponse(
                    AssetId:           Guid.Parse(reader.GetString(0)),
                    WorkId:            Guid.Parse(reader.GetString(1)),
                    HubId:             reader.IsDBNull(2) ? null : Guid.Parse(reader.GetString(2)),
                    MediaType:         reader.GetString(3),
                    ProgressPct:       reader.GetDouble(4),
                    LastAccessed:      DateTimeOffset.Parse(reader.GetString(5)),
                    ExtendedProperties: extProps,
                    HubDisplayName:    reader.IsDBNull(7) ? null : reader.GetString(7),
                    Title:             reader.IsDBNull(8)
                                           ? (reader.IsDBNull(7) ? "Untitled" : reader.GetString(7))
                                           : reader.GetString(8),
                    Author:            reader.IsDBNull(9)  ? null : reader.GetString(9),
                    CoverUrl:          reader.IsDBNull(10) ? null : reader.GetString(10),
                    Narrator:          reader.IsDBNull(11) ? null : reader.GetString(11),
                    Series:            reader.IsDBNull(12) ? null : reader.GetString(12),
                    SeriesPosition:    reader.IsDBNull(13) ? null : reader.GetString(13),
                    Description:       reader.IsDBNull(14) ? null : reader.GetString(14)));
            }

            return Results.Ok(results);
        });

        return app;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Resolves a user ID from a query string. Falls back to a deterministic
    /// default GUID until multi-user authentication is implemented.
    /// </summary>
    private static Guid ResolveUserId(string? userId) =>
        Guid.TryParse(userId, out var parsed)
            ? parsed
            : Guid.Parse("00000000-0000-0000-0000-000000000001"); // default owner

    private static object MapStateResponse(UserState s) => new
    {
        user_id             = s.UserId,
        asset_id            = s.AssetId,
        content_hash        = s.ContentHash,
        progress_pct        = s.ProgressPct,
        last_accessed       = s.LastAccessed,
        extended_properties = s.ExtendedProperties,
    };
}

// ── Request / response DTOs ─────────────────────────────────────────────────

public sealed record ProgressUpdateRequest(
    [property: JsonPropertyName("user_id")]             string?                       UserId,
    [property: JsonPropertyName("progress_pct")]        double                        ProgressPct,
    [property: JsonPropertyName("extended_properties")] Dictionary<string, string>?   ExtendedProperties);

public sealed record JourneyItemResponse(
    Guid                         AssetId,
    Guid                         WorkId,
    Guid?                        HubId,
    string                       Title,
    string?                      Author,
    string?                      CoverUrl,
    string?                      Narrator,
    string?                      Series,
    string?                      SeriesPosition,
    string?                      Description,
    string                       MediaType,
    double                       ProgressPct,
    DateTimeOffset               LastAccessed,
    string?                      HubDisplayName,
    Dictionary<string, string>   ExtendedProperties);
