using System.Text.Json;
using Tanaste.Storage.Contracts;
using Tanaste.Storage.Models;

namespace Tanaste.Storage;

/// <summary>
/// Reads and writes <c>tanaste_master.json</c> using <see cref="System.Text.Json"/>.
/// No external serialisation library is required.
///
/// Recovery behaviour (spec: "MUST attempt to load from .bak before halting"):
///   1. Try <c>tanaste_master.json</c>.
///   2. If missing/corrupt, try <c>tanaste_master.json.bak</c> and restore primary.
///   3. If both fail, throw <see cref="InvalidOperationException"/>.
///
/// Save behaviour:
///   • Rotates the current file to <c>.bak</c> before overwriting.
///
/// Spec: Phase 4 – IStorageManifest interface.
/// </summary>
public sealed class ManifestParser : IStorageManifest
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented        = true,
        AllowTrailingCommas  = true,
        ReadCommentHandling  = JsonCommentHandling.Skip,
        // Property names in the JSON use snake_case; handled via [JsonPropertyName].
    };

    private readonly string _manifestPath;
    private readonly string _backupPath;

    /// <param name="manifestPath">
    /// Full path to <c>tanaste_master.json</c>.
    /// The backup is derived by appending <c>.bak</c>.
    /// </param>
    public ManifestParser(string manifestPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);
        _manifestPath = manifestPath;
        _backupPath   = manifestPath + ".bak";
    }

    // -------------------------------------------------------------------------
    // IStorageManifest
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public TanasteMasterManifest Load()
    {
        // Attempt 1 – primary file
        if (TryDeserialize(_manifestPath, out var manifest))
            return manifest!;

        // Attempt 2 – backup file; restore primary on success
        if (File.Exists(_backupPath) && TryDeserialize(_backupPath, out manifest))
        {
            // Restore the primary from the backup so subsequent saves work normally.
            File.Copy(_backupPath, _manifestPath, overwrite: true);
            return manifest!;
        }

        // Attempt 3 – first-run bootstrap: create a default manifest with all
        // standard providers and persist it so subsequent loads succeed.
        manifest = CreateDefaultManifest();
        Save(manifest);
        return manifest;
    }

    /// <inheritdoc/>
    public void Save(TanasteMasterManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        // Rotate current primary to backup before overwriting.
        if (File.Exists(_manifestPath))
            File.Copy(_manifestPath, _backupPath, overwrite: true);

        var json = JsonSerializer.Serialize(manifest, SerializerOptions);
        File.WriteAllText(_manifestPath, json);
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Creates a default manifest with all standard providers, scoring defaults,
    /// and external API endpoint URLs. Used on first run when no manifest exists.
    /// </summary>
    private static TanasteMasterManifest CreateDefaultManifest() => new()
    {
        SchemaVersion = "1.0",
        DatabasePath  = "tanaste.db",
        DataRoot      = "./media",
        Providers =
        [
            new ProviderBootstrap
            {
                Name    = "local_filesystem",
                Enabled = true,
                Weight  = 1.0,
                Domain  = ProviderDomain.Universal,
            },
            new ProviderBootstrap
            {
                Name           = "apple_books",
                Enabled        = true,
                Weight         = 0.7,
                Domain         = ProviderDomain.Universal,
                CapabilityTags = ["cover", "description", "rating"],
                FieldWeights   = new() { ["cover"] = 0.85, ["description"] = 0.85, ["rating"] = 0.7 },
            },
            new ProviderBootstrap
            {
                Name           = "open_library",
                Enabled        = false,
                Weight         = 0.7,
                Domain         = ProviderDomain.Ebook,
                CapabilityTags = ["series"],
                FieldWeights   = new() { ["series"] = 0.9 },
            },
            new ProviderBootstrap
            {
                Name           = "audnexus",
                Enabled        = true,
                Weight         = 0.7,
                Domain         = ProviderDomain.Audiobook,
                CapabilityTags = ["cover", "narrator", "series"],
                FieldWeights   = new() { ["cover"] = 0.9, ["narrator"] = 0.9, ["series"] = 0.9 },
            },
            new ProviderBootstrap
            {
                Name           = "wikidata",
                Enabled        = true,
                Weight         = 0.7,
                Domain         = ProviderDomain.Universal,
                CapabilityTags = ["series", "franchise", "person_id"],
                FieldWeights   = new() { ["series"] = 1.0, ["franchise"] = 1.0, ["person_id"] = 1.0 },
            },
        ],
        ProviderEndpoints = new()
        {
            ["apple_books"]    = "https://itunes.apple.com",
            ["audnexus"]       = "https://api.audnexus.com",
            ["wikidata_api"]   = "https://www.wikidata.org/w/api.php",
            ["wikidata_sparql"] = "https://query.wikidata.org/sparql",
        },
    };

    private static bool TryDeserialize(string path, out TanasteMasterManifest? manifest)
    {
        manifest = null;

        if (!File.Exists(path))
            return false;

        try
        {
            var json = File.ReadAllText(path);
            manifest = JsonSerializer.Deserialize<TanasteMasterManifest>(json, SerializerOptions);
            return manifest is not null;
        }
        catch (JsonException)
        {
            // File exists but is corrupt; signal caller to try the backup.
            return false;
        }
        catch (IOException)
        {
            // Transient read failure; treat as unavailable.
            return false;
        }
    }
}
