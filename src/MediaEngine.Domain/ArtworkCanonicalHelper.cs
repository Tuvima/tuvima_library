using MediaEngine.Domain.Entities;

namespace MediaEngine.Domain;

/// <summary>
/// Helper methods for persisting artwork-truth canonicals.
/// </summary>
public static class ArtworkCanonicalHelper
{
    public static List<CanonicalValue> CreateFlags(
        Guid entityId,
        string coverState,
        string? coverSource,
        string heroState,
        DateTimeOffset lastScoredAt,
        bool settled)
    {
        var values = new List<CanonicalValue>
        {
            Create(entityId, MetadataFieldConstants.CoverState, coverState, lastScoredAt),
            Create(entityId, MetadataFieldConstants.HeroState, heroState, lastScoredAt),
        };

        if (!string.IsNullOrWhiteSpace(coverSource))
            values.Add(Create(entityId, MetadataFieldConstants.CoverSource, coverSource, lastScoredAt));

        if (settled)
            values.Add(Create(entityId, MetadataFieldConstants.ArtworkSettledAt, lastScoredAt.ToString("o"), lastScoredAt));

        return values;
    }

    public static CanonicalValue Create(Guid entityId, string key, string value, DateTimeOffset lastScoredAt) => new()
    {
        EntityId = entityId,
        Key = key,
        Value = value,
        LastScoredAt = lastScoredAt,
    };

    public static List<CanonicalValue> CreatePreferredAssetCanonicals(
        Guid entityId,
        EntityAsset asset,
        DateTimeOffset lastScoredAt)
    {
        ArgumentNullException.ThrowIfNull(asset);

        var values = new List<CanonicalValue>();
        var baseUrl = $"/stream/artwork/{asset.Id}";
        var smallUrl = $"/stream/artwork/{asset.Id}?size=s";
        var mediumUrl = $"/stream/artwork/{asset.Id}?size=m";
        var largeUrl = $"/stream/artwork/{asset.Id}?size=l";

        foreach (var key in ResolvePrimaryUrlKeys(asset.AssetTypeValue))
        {
            values.Add(Create(entityId, key, baseUrl, lastScoredAt));
        }

        var prefix = ResolveMetadataPrefix(asset.AssetTypeValue);
        if (!string.IsNullOrWhiteSpace(prefix))
        {
            values.Add(Create(entityId, $"{prefix}_url_s", smallUrl, lastScoredAt));
            values.Add(Create(entityId, $"{prefix}_url_m", mediumUrl, lastScoredAt));
            values.Add(Create(entityId, $"{prefix}_url_l", largeUrl, lastScoredAt));

            if (!string.IsNullOrWhiteSpace(asset.AspectClass))
            {
                values.Add(Create(entityId, $"{prefix}_aspect_class", asset.AspectClass, lastScoredAt));
            }

            if (asset.WidthPx is > 0)
            {
                values.Add(Create(entityId, $"{prefix}_width_px", asset.WidthPx.Value.ToString(), lastScoredAt));
            }

            if (asset.HeightPx is > 0)
            {
                values.Add(Create(entityId, $"{prefix}_height_px", asset.HeightPx.Value.ToString(), lastScoredAt));
            }

            if (!string.IsNullOrWhiteSpace(asset.PrimaryHex))
            {
                values.Add(Create(entityId, $"{prefix}_primary_hex", asset.PrimaryHex, lastScoredAt));
            }

            if (!string.IsNullOrWhiteSpace(asset.SecondaryHex))
            {
                values.Add(Create(entityId, $"{prefix}_secondary_hex", asset.SecondaryHex, lastScoredAt));
            }

            if (!string.IsNullOrWhiteSpace(asset.AccentHex))
            {
                values.Add(Create(entityId, $"{prefix}_accent_hex", asset.AccentHex, lastScoredAt));
            }
        }

        if (!string.IsNullOrWhiteSpace(asset.PrimaryHex))
        {
            values.Add(Create(entityId, MetadataFieldConstants.ArtworkPrimaryHex, asset.PrimaryHex, lastScoredAt));
        }

        if (!string.IsNullOrWhiteSpace(asset.SecondaryHex))
        {
            values.Add(Create(entityId, MetadataFieldConstants.ArtworkSecondaryHex, asset.SecondaryHex, lastScoredAt));
        }

        if (!string.IsNullOrWhiteSpace(asset.AccentHex))
        {
            values.Add(Create(entityId, MetadataFieldConstants.ArtworkAccentHex, asset.AccentHex, lastScoredAt));
            values.Add(Create(entityId, "dominant_color", asset.AccentHex, lastScoredAt));
        }

        return values;
    }

    private static IReadOnlyList<string> ResolvePrimaryUrlKeys(string assetTypeValue) => assetTypeValue switch
    {
        "CoverArt" => ["cover_url"],
        "Background" => ["background", "background_url"],
        "Banner" => ["banner", "banner_url"],
        "Logo" => ["logo", "logo_url"],
        "SquareArt" => ["square", "square_url"],
        "Headshot" => ["artist_photo_url", "headshot_url"],
        "SeasonPoster" => ["season_poster", "season_poster_url"],
        "SeasonThumb" => ["season_thumb", "season_thumb_url"],
        "EpisodeStill" => ["episode_still", "episode_still_url"],
        "CharacterPortrait" => ["character_portrait", "character_portrait_url"],
        "DiscArt" => ["disc", "disc_art_url"],
        "ClearArt" => ["clearart", "clear_art_url"],
        _ => [],
    };

    private static string? ResolveMetadataPrefix(string assetTypeValue) => assetTypeValue switch
    {
        "CoverArt" => "cover",
        "Background" => "background",
        "Banner" => "banner",
        "Logo" => "logo",
        "SquareArt" => "square",
        "Headshot" => "artist_photo",
        "SeasonPoster" => "season_poster",
        "SeasonThumb" => "season_thumb",
        "EpisodeStill" => "episode_still",
        "CharacterPortrait" => "character_portrait",
        "DiscArt" => "disc_art",
        "ClearArt" => "clear_art",
        _ => null,
    };
}
