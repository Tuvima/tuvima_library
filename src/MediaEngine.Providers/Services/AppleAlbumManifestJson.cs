using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MediaEngine.Providers.Services;

/// <summary>
/// Owns the persisted Apple album-track manifest contract shared by ingestion and
/// the API repair sweep. The top-level collection identity prevents stale box-set
/// tracks from being merged into a newly matched album.
/// </summary>
public static class AppleAlbumManifestJson
{
    public const string Source = "apple_itunes_album";

    public static string Build(
        IReadOnlyList<JsonNode> tracks,
        string collectionId,
        string? album,
        string? artist)
    {
        var array = new JsonArray();
        var ordinal = 0;
        foreach (var track in tracks)
        {
            var title = track["trackName"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(title))
                continue;

            ordinal++;
            var trackNumber = track["trackNumber"]?.GetValue<int?>() ?? ordinal;
            var durationMillis = track["trackTimeMillis"]?.GetValue<long?>();
            var item = new JsonObject
            {
                ["title"] = title,
                ["ordinal"] = trackNumber,
                ["track_number"] = trackNumber,
                ["source"] = "apple_itunes",
            };

            if (track["discNumber"]?.GetValue<int?>() is { } discNumber)
                item["disc_number"] = discNumber;

            if (durationMillis is > 0)
                item["duration_seconds"] = Math.Round(durationMillis.Value / 1000d, 3);

            if (track["trackId"]?.GetValue<long?>() is { } trackId)
                item["apple_music_id"] = trackId.ToString(CultureInfo.InvariantCulture);

            array.Add(item);
        }

        return new JsonObject
        {
            ["schema"] = "music_album_tracks_v1",
            ["source"] = Source,
            ["provider_collection_id"] = collectionId,
            ["album"] = album,
            ["artist"] = artist,
            ["tracks"] = array,
        }.ToJsonString();
    }

    public static bool IsCompleteForCollection(string? json, string? collectionId = null)
    {
        if (string.IsNullOrWhiteSpace(json))
            return false;

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!root.TryGetProperty("source", out var source)
                || !string.Equals(source.GetString(), Source, StringComparison.OrdinalIgnoreCase)
                || !root.TryGetProperty("provider_collection_id", out var providerCollection)
                || string.IsNullOrWhiteSpace(providerCollection.GetString())
                || (!string.IsNullOrWhiteSpace(collectionId)
                    && !string.Equals(providerCollection.GetString(), collectionId, StringComparison.OrdinalIgnoreCase))
                || !root.TryGetProperty("tracks", out var tracks)
                || tracks.ValueKind != JsonValueKind.Array
                || tracks.GetArrayLength() == 0)
            {
                return false;
            }

            return tracks.EnumerateArray().All(track =>
                track.TryGetProperty("title", out var title)
                && !string.IsNullOrWhiteSpace(title.GetString())
                && TryReadPositiveInt(track, "ordinal")
                && TryReadPositiveInt(track, "track_number")
                && track.TryGetProperty("duration_seconds", out var duration)
                && duration.TryGetDouble(out var seconds)
                && seconds > 0);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static bool ContainsAppleTrackRows(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return false;

        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty("tracks", out var tracks)
                && tracks.ValueKind == JsonValueKind.Array
                && tracks.EnumerateArray().Any(track =>
                    track.TryGetProperty("source", out var source)
                    && source.ValueKind == JsonValueKind.String
                    && source.GetString()!.StartsWith("apple_", StringComparison.OrdinalIgnoreCase));
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryReadPositiveInt(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var value)
            && value.TryGetInt32(out var number)
            && number > 0;
}
