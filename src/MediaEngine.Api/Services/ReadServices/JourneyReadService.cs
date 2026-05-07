using System.Data;
using System.Text.Json;
using MediaEngine.Application.ReadModels;
using MediaEngine.Application.Services;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Api.Services.ReadServices;

public sealed class JourneyReadService : IJourneyReadService
{
    private readonly IDatabaseConnection _db;

    public JourneyReadService(IDatabaseConnection db)
    {
        _db = db;
    }

    public Task<IReadOnlyList<JourneyItemResponse>> GetJourneyAsync(
        Guid userId,
        Guid? collectionId,
        int limit,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = collectionId is null
            ? BaseSelect + "\nORDER BY us.last_accessed DESC\nLIMIT @limit;"
            : BaseSelect + "\n  AND w.collection_id = @collectionId\nORDER BY us.last_accessed DESC\nLIMIT @limit;";

        cmd.Parameters.AddWithValue("@userId", userId.ToString());
        cmd.Parameters.AddWithValue("@limit", limit);
        if (collectionId is not null)
            cmd.Parameters.AddWithValue("@collectionId", collectionId.Value.ToString());

        using var reader = cmd.ExecuteReader();
        var results = new List<JourneyItemResponse>();
        while (reader.Read())
            results.Add(Map(reader));

        return Task.FromResult<IReadOnlyList<JourneyItemResponse>>(results);
    }

    private static JourneyItemResponse Map(IDataRecord reader)
    {
        var extJson = reader.IsDBNull(6) ? null : reader.GetString(6);
        var extProps = string.IsNullOrEmpty(extJson)
            ? new Dictionary<string, string>()
            : JsonSerializer.Deserialize<Dictionary<string, string>>(extJson)
              ?? new Dictionary<string, string>();

        return new JourneyItemResponse(
            AssetId: Guid.Parse(reader.GetString(0)),
            WorkId: Guid.Parse(reader.GetString(1)),
            CollectionId: reader.IsDBNull(2) ? null : Guid.Parse(reader.GetString(2)),
            MediaType: reader.GetString(3),
            ProgressPct: reader.GetDouble(4),
            LastAccessed: DateTimeOffset.Parse(reader.GetString(5)),
            ExtendedProperties: extProps,
            CollectionDisplayName: reader.IsDBNull(7) ? null : reader.GetString(7),
            Title: reader.IsDBNull(8)
                ? (reader.IsDBNull(7) ? "Untitled" : reader.GetString(7))
                : reader.GetString(8),
            Author: reader.IsDBNull(9) ? null : reader.GetString(9),
            CoverUrl: reader.IsDBNull(10) ? null : reader.GetString(10),
            BackgroundUrl: reader.IsDBNull(11) ? null : reader.GetString(11),
            BannerUrl: reader.IsDBNull(12) ? null : reader.GetString(12),
            LogoUrl: reader.IsDBNull(13) ? null : reader.GetString(13),
            CoverWidthPx: ReadNullableInt(reader, 14),
            CoverHeightPx: ReadNullableInt(reader, 15),
            BackgroundWidthPx: ReadNullableInt(reader, 16),
            BackgroundHeightPx: ReadNullableInt(reader, 17),
            BannerWidthPx: ReadNullableInt(reader, 18),
            BannerHeightPx: ReadNullableInt(reader, 19),
            Narrator: reader.IsDBNull(20) ? null : reader.GetString(20),
            Series: reader.IsDBNull(21) ? null : reader.GetString(21),
            SeriesPosition: reader.IsDBNull(22) ? null : reader.GetString(22),
            Description: reader.IsDBNull(23) ? null : reader.GetString(23),
            HeroUrl: null);
    }

    private static int? ReadNullableInt(IDataRecord reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
            return null;

        var raw = reader.GetValue(ordinal);
        return raw switch
        {
            int value when value > 0 => value,
            long value when value > 0 => (int)value,
            string value when int.TryParse(value, out var parsed) && parsed > 0 => parsed,
            _ => null,
        };
    }

    private const string BaseSelect = """
        SELECT
            us.asset_id,
            w.id            AS work_id,
            w.collection_id,
            w.media_type,
            us.progress_pct,
            us.last_accessed,
            us.extended_properties,
            h.display_name  AS collection_display_name,
            cv_title_a.value      AS title,
            cv_author_w.value     AS author,
            cv_cover_w.value      AS cover_url,
            cv_background_w.value AS background_url,
            cv_banner_w.value     AS banner_url,
            cv_logo_w.value       AS logo_url,
            cv_cover_width_w.value      AS cover_width_px,
            cv_cover_height_w.value     AS cover_height_px,
            cv_background_width_w.value AS background_width_px,
            cv_background_height_w.value AS background_height_px,
            cv_banner_width_w.value     AS banner_width_px,
            cv_banner_height_w.value    AS banner_height_px,
            cv_narrator_w.value   AS narrator,
            cv_series_w.value     AS series,
            cv_series_pos_a.value AS series_position,
            cv_desc_w.value       AS description
        FROM user_states us
        JOIN media_assets ma ON ma.id = us.asset_id
        JOIN editions e      ON e.id  = ma.edition_id
        JOIN works w         ON w.id  = e.work_id
        LEFT JOIN works pw   ON pw.id  = w.parent_work_id
        LEFT JOIN works gpw  ON gpw.id = pw.parent_work_id
        LEFT JOIN collections h     ON h.id  = w.collection_id
        LEFT JOIN canonical_values cv_title_a
            ON cv_title_a.entity_id = ma.id AND cv_title_a.key = 'title'
        LEFT JOIN canonical_values cv_series_pos_a
            ON cv_series_pos_a.entity_id = ma.id AND cv_series_pos_a.key = 'series_position'
        LEFT JOIN canonical_values cv_author_w
            ON cv_author_w.entity_id = COALESCE(gpw.id, pw.id, w.id)
           AND cv_author_w.key = 'author'
        LEFT JOIN canonical_values cv_cover_w
            ON cv_cover_w.entity_id = COALESCE(gpw.id, pw.id, w.id)
           AND cv_cover_w.key = 'cover'
        LEFT JOIN canonical_values cv_narrator_w
            ON cv_narrator_w.entity_id = COALESCE(gpw.id, pw.id, w.id)
           AND cv_narrator_w.key = 'narrator'
        LEFT JOIN canonical_values cv_background_w
            ON cv_background_w.entity_id = COALESCE(gpw.id, pw.id, w.id)
           AND cv_background_w.key = 'background'
        LEFT JOIN canonical_values cv_banner_w
            ON cv_banner_w.entity_id = COALESCE(gpw.id, pw.id, w.id)
           AND cv_banner_w.key = 'banner'
        LEFT JOIN canonical_values cv_logo_w
            ON cv_logo_w.entity_id = COALESCE(gpw.id, pw.id, w.id)
           AND cv_logo_w.key = 'logo'
        LEFT JOIN canonical_values cv_cover_width_w
            ON cv_cover_width_w.entity_id = COALESCE(gpw.id, pw.id, w.id)
           AND cv_cover_width_w.key = 'cover_width_px'
        LEFT JOIN canonical_values cv_cover_height_w
            ON cv_cover_height_w.entity_id = COALESCE(gpw.id, pw.id, w.id)
           AND cv_cover_height_w.key = 'cover_height_px'
        LEFT JOIN canonical_values cv_background_width_w
            ON cv_background_width_w.entity_id = COALESCE(gpw.id, pw.id, w.id)
           AND cv_background_width_w.key = 'background_width_px'
        LEFT JOIN canonical_values cv_background_height_w
            ON cv_background_height_w.entity_id = COALESCE(gpw.id, pw.id, w.id)
           AND cv_background_height_w.key = 'background_height_px'
        LEFT JOIN canonical_values cv_banner_width_w
            ON cv_banner_width_w.entity_id = COALESCE(gpw.id, pw.id, w.id)
           AND cv_banner_width_w.key = 'banner_width_px'
        LEFT JOIN canonical_values cv_banner_height_w
            ON cv_banner_height_w.entity_id = COALESCE(gpw.id, pw.id, w.id)
           AND cv_banner_height_w.key = 'banner_height_px'
        LEFT JOIN canonical_values cv_series_w
            ON cv_series_w.entity_id = COALESCE(gpw.id, pw.id, w.id)
           AND cv_series_w.key = 'series'
        LEFT JOIN canonical_values cv_desc_w
            ON cv_desc_w.entity_id = COALESCE(gpw.id, pw.id, w.id)
           AND cv_desc_w.key = 'description'
        WHERE us.user_id = @userId
          AND us.progress_pct > 0.0
          AND us.progress_pct < 100.0
        """;
}
