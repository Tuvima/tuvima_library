using System.IO.Compression;
using System.Text.Json;

namespace MediaEngine.Api.Services;

internal static class ZipArchiveJson
{
    public static JsonDocument Parse(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        return JsonDocument.Parse(stream);
    }
}
