using System.Text.Json;

namespace MediaEngine.Plugin.FandomLore;

internal static class FandomSettings
{
    public static bool ReadBool(IReadOnlyDictionary<string, JsonElement> settings, string key, bool fallback)
        => settings.TryGetValue(key, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : fallback;

    public static string ReadString(IReadOnlyDictionary<string, JsonElement> settings, string key, string fallback)
        => settings.TryGetValue(key, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
            : fallback;

    public static int ReadInt(IReadOnlyDictionary<string, JsonElement> settings, string key, int fallback)
        => settings.TryGetValue(key, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var parsed)
            ? parsed
            : fallback;

    public static double ReadDouble(IReadOnlyDictionary<string, JsonElement> settings, string key, double fallback)
        => settings.TryGetValue(key, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var parsed)
            ? parsed
            : fallback;
}
