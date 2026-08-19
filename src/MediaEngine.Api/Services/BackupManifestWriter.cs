using System.IO.Compression;
using System.Text.Json;

namespace MediaEngine.Api.Services;

internal static class BackupManifestWriter
{
    public static void Write(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        JsonSerializer.Serialize(stream, new
        {
            schema_version = "1.0",
            created_at = DateTimeOffset.UtcNow,
            database_epoch = "guid-blob-v1",
            includes_secrets = false,
            note = "Provider secrets are intentionally excluded. Re-enter credentials after restore if needed.",
        });
    }
}
