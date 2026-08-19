using System.Text.Json.Serialization;

namespace MediaEngine.Contracts.Settings;

public sealed class AuthSettingsDto
{
    [JsonPropertyName("mode")]
    public string Mode { get; init; } = "DisabledLocalOnly";

    [JsonPropertyName("localhost_bypass")]
    public bool LocalhostBypass { get; init; } = true;

    [JsonPropertyName("require_https_remote")]
    public bool RequireHttpsRemote { get; init; }

    [JsonPropertyName("oidc_enabled")]
    public bool OidcEnabled { get; init; }

    [JsonPropertyName("oidc_display_name")]
    public string OidcDisplayName { get; init; } = "OpenID Connect";

    [JsonPropertyName("oidc_authority")]
    public string OidcAuthority { get; init; } = string.Empty;

    [JsonPropertyName("oidc_client_id")]
    public string OidcClientId { get; init; } = string.Empty;

    [JsonPropertyName("oidc_scopes")]
    public List<string> OidcScopes { get; init; } = [];
}

public sealed record ServerGeneralSettingsDto(
    [property: JsonPropertyName("server_name")] string ServerName = "Tuvima Library",
    [property: JsonPropertyName("language")] string Language = "en",
    [property: JsonPropertyName("display_language")] string DisplayLanguage = "en",
    [property: JsonPropertyName("metadata_language")] string MetadataLanguage = "en",
    [property: JsonPropertyName("additional_languages")] List<string>? AdditionalLanguages = null,
    [property: JsonPropertyName("accept_any_language")] bool AcceptAnyLanguage = true,
    [property: JsonPropertyName("country")] string Country = "US",
    [property: JsonPropertyName("date_format")] string DateFormat = "system",
    [property: JsonPropertyName("time_format")] string TimeFormat = "system");

public sealed class FolderSettingsDto
{
    public FolderSettingsDto()
    {
    }

    public FolderSettingsDto(List<string>? WatchDirectories)
    {
        this.WatchDirectories = WatchDirectories ?? [];
    }

    [JsonPropertyName("watch_directories")]
    public List<string> WatchDirectories { get; set; } = [];
}

public sealed class LibraryFolderDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "catalogued";

    [JsonPropertyName("metadata_policy")]
    public string MetadataPolicy { get; set; } = "enriched";

    [JsonPropertyName("media_types")]
    public List<string> MediaTypes { get; set; } = [];

    [JsonPropertyName("source_paths")]
    public List<string> SourcePaths { get; set; } = [];

    [JsonPropertyName("library_root")]
    public string? LibraryRoot { get; set; }

    [JsonPropertyName("intake_mode")]
    public string IntakeMode { get; set; } = "watch";

    [JsonPropertyName("include_subdirectories")]
    public bool IncludeSubdirectories { get; set; } = true;

    [JsonPropertyName("read_only")]
    public bool ReadOnly { get; set; }

    [JsonPropertyName("writeback_override")]
    public bool? WritebackOverride { get; set; }

    [JsonPropertyName("notes")]
    public string? Notes { get; set; }
}

public sealed class UpdateLibrariesRequest
{
    [JsonPropertyName("libraries")]
    public List<LibraryFolderDto> Libraries { get; init; } = [];
}

public sealed class TestPathRequest
{
    [JsonPropertyName("path")]
    public string Path { get; init; } = string.Empty;
}

public sealed class PathTestResultDto
{
    public PathTestResultDto()
    {
    }

    public PathTestResultDto(string path, bool exists, bool hasRead, bool hasWrite)
    {
        Path = path;
        Exists = exists;
        HasRead = hasRead;
        HasWrite = hasWrite;
    }

    [JsonPropertyName("path")]
    public string Path { get; init; } = string.Empty;

    [JsonPropertyName("exists")]
    public bool Exists { get; init; }

    [JsonPropertyName("has_read")]
    public bool HasRead { get; init; }

    [JsonPropertyName("has_write")]
    public bool HasWrite { get; init; }
}

public sealed class BrowseDirectoryRequest
{
    [JsonPropertyName("path")]
    public string? Path { get; init; }
}

public sealed class BrowseDirectoryResultDto
{
    public BrowseDirectoryResultDto()
    {
    }

    public BrowseDirectoryResultDto(string currentPath, string? parentPath, List<string> directories)
    {
        CurrentPath = currentPath;
        ParentPath = parentPath;
        Directories = directories;
    }

    [JsonPropertyName("current_path")]
    public string CurrentPath { get; init; } = string.Empty;

    [JsonPropertyName("parent_path")]
    public string? ParentPath { get; init; }

    [JsonPropertyName("directories")]
    public List<string> Directories { get; init; } = [];
}

public sealed class OrganizationTemplateDto
{
    public OrganizationTemplateDto()
    {
    }

    public OrganizationTemplateDto(
        string template,
        string? preview,
        Dictionary<string, string>? templates = null)
    {
        Template = template;
        Preview = preview;
        Templates = templates;
    }

    [JsonPropertyName("template")]
    public string Template { get; init; } = string.Empty;

    [JsonPropertyName("preview")]
    public string? Preview { get; init; }

    [JsonPropertyName("templates")]
    public Dictionary<string, string>? Templates { get; init; }
}

public sealed class UpdateOrganizationTemplateRequest
{
    [JsonPropertyName("template")]
    public string Template { get; init; } = string.Empty;

    [JsonPropertyName("templates")]
    public Dictionary<string, string>? Templates { get; init; }
}
