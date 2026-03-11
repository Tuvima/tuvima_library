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

        // GET /progress/journey?userId={userId}&limit=5 — incomplete items with
        // Work+Hub context for "Continue your Journey" hero.
        group.MapGet("/journey", async (
            string? userId,
            int? limit,
            IDatabaseConnection db,
            CancellationToken ct) =>
        {
            ct.ThrowIfCancellationRequested();
            var uid = ResolveUserId(userId);
            var conn = db.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT
                    us.asset_id,
                    w.id            AS work_id,
                    w.hub_id,
                    w.media_type,
                    us.progress_pct,
                    us.last_accessed,
                    us.extended_properties,
                    h.display_name  AS hub_display_name,
                    cv_title.value  AS title,
                    cv_author.value AS author,
                    cv_cover.value  AS cover_url,
                    cv_narrator.value AS narrator,
                    cv_series.value AS series,
                    cv_series_pos.value AS series_position,
                    cv_desc.value   AS description
                FROM user_states us
                JOIN media_assets ma ON ma.id = us.asset_id
                JOIN editions e      ON e.id  = ma.edition_id
                JOIN works w         ON w.id  = e.work_id
                LEFT JOIN hubs h     ON h.id  = w.hub_id
                LEFT JOIN canonical_values cv_title
                    ON cv_title.entity_id = w.id AND cv_title.key = 'title'
                LEFT JOIN canonical_values cv_author
                    ON cv_author.entity_id = w.id AND cv_author.key = 'author'
                LEFT JOIN canonical_values cv_cover
                    ON cv_cover.entity_id = w.id AND cv_cover.key = 'cover'
                LEFT JOIN canonical_values cv_narrator
                    ON cv_narrator.entity_id = w.id AND cv_narrator.key = 'narrator'
                LEFT JOIN canonical_values cv_series
                    ON cv_series.entity_id = w.id AND cv_series.key = 'series'
                LEFT JOIN canonical_values cv_series_pos
                    ON cv_series_pos.entity_id = w.id AND cv_series_pos.key = 'series_position'
                LEFT JOIN canonical_values cv_desc
                    ON cv_desc.entity_id = w.id AND cv_desc.key = 'description'
                WHERE us.user_id = @userId
                  AND us.progress_pct > 0.0
                  AND us.progress_pct < 100.0
                ORDER BY us.last_accessed DESC
                LIMIT @limit;
                """;
            cmd.Parameters.AddWithValue("@userId", uid.ToString());
            cmd.Parameters.AddWithValue("@limit", limit ?? 5);

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
                    Title:             reader.IsDBNull(8) ? "Untitled" : reader.GetString(8),
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
