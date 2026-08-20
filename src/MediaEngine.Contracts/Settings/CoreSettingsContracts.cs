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

public sealed class LibraryFolderDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("category")]
    public string? Category { get; set; }

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "catalogued";

    [JsonPropertyName("area")]
    public string Area { get; set; } = "read";

    [JsonPropertyName("presentation")]
    public string Presentation { get; set; } = "catalogue";

    [JsonPropertyName("metadata_policy")]
    public string MetadataPolicy { get; set; } = "enriched";

    [JsonPropertyName("media_types")]
    public List<string> MediaTypes { get; set; } = [];

    [JsonPropertyName("sources")]
    public List<LibrarySourceDto> Sources { get; set; } = [];

    [JsonPropertyName("primary_destination_source_id")]
    public string? PrimaryDestinationSourceId { get; set; }

    [JsonPropertyName("owner_profile_id")]
    public string? OwnerProfileId { get; set; }

    [JsonPropertyName("visibility")]
    public string Visibility { get; set; } = "household";

    [JsonPropertyName("authorized_profile_ids")]
    public List<string> AuthorizedProfileIds { get; set; } = [];

    [JsonPropertyName("accepted_intake_modes")]
    public List<string> AcceptedIntakeModes { get; set; } = [];

    [JsonPropertyName("duplicate_policy")]
    public string DuplicatePolicy { get; set; } = "skip_exact";

    [JsonPropertyName("organization_policy")]
    public LibraryOrganizationPolicyDto OrganizationPolicy { get; set; } = new();

    [JsonPropertyName("notes")]
    public string? Notes { get; set; }
}

public sealed class LibrarySourceDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("role")]
    public string Role { get; set; } = "secondary";

    [JsonPropertyName("management_mode")]
    public string ManagementMode { get; set; } = "existing_library";

    [JsonPropertyName("source_type")]
    public string SourceType { get; set; } = "local_folder";

    [JsonPropertyName("include_subdirectories")]
    public bool IncludeSubdirectories { get; set; } = true;

    [JsonPropertyName("access_mode")]
    public string AccessMode { get; set; } = "read_only";

    [JsonPropertyName("writeback_override")]
    public bool? WritebackOverride { get; set; }

    [JsonPropertyName("participates_in_organization")]
    public bool ParticipatesInOrganization { get; set; }

    [JsonPropertyName("intake_role")]
    public string IntakeRole { get; set; } = "none";

    [JsonPropertyName("notes")]
    public string? Notes { get; set; }

    [JsonPropertyName("device_id")]
    public string? DeviceId { get; set; }

    [JsonPropertyName("profile_id")]
    public string? ProfileId { get; set; }
}

public sealed class LibraryOrganizationPolicyDto
{
    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "tuvima_standard";

    [JsonPropertyName("custom_template")]
    public string? CustomTemplate { get; set; }

    [JsonPropertyName("preserve_originals")]
    public bool PreserveOriginals { get; set; } = true;
}

public sealed class UpdateLibrariesRequest
{
    [JsonPropertyName("schema_version")]
    public string SchemaVersion { get; init; } = "3.0";

    [JsonPropertyName("libraries")]
    public List<LibraryFolderDto> Libraries { get; init; } = [];

    [JsonPropertyName("incoming_sources")]
    public List<IncomingSourceDto> IncomingSources { get; init; } = [];

    [JsonPropertyName("personal_library_policy")]
    public PersonalLibraryPolicyDto PersonalLibraryPolicy { get; init; } = new();
}

public sealed class LibrariesConfigurationDto
{
    [JsonPropertyName("schema_version")]
    public string SchemaVersion { get; init; } = "3.0";

    [JsonPropertyName("libraries")]
    public List<LibraryFolderDto> Libraries { get; init; } = [];

    [JsonPropertyName("incoming_sources")]
    public List<IncomingSourceDto> IncomingSources { get; init; } = [];

    [JsonPropertyName("personal_library_policy")]
    public PersonalLibraryPolicyDto PersonalLibraryPolicy { get; init; } = new();
}

public sealed class PersonalLibraryPolicyDto
{
    [JsonPropertyName("allow_user_creation")]
    public bool AllowUserCreation { get; set; } = true;

    [JsonPropertyName("allow_mobile_backup")]
    public bool AllowMobileBackup { get; set; } = true;

    [JsonPropertyName("allow_browser_upload")]
    public bool AllowBrowserUpload { get; set; } = true;

    [JsonPropertyName("allow_drag_and_drop")]
    public bool AllowDragAndDrop { get; set; } = true;

    [JsonPropertyName("allow_connected_device_import")]
    public bool AllowConnectedDeviceImport { get; set; } = true;

    [JsonPropertyName("allow_managed_storage")]
    public bool AllowManagedStorage { get; set; } = true;

    [JsonPropertyName("allow_existing_folder_attachment")]
    public bool AllowExistingFolderAttachment { get; set; } = true;

    [JsonPropertyName("default_visibility")]
    public string DefaultVisibility { get; set; } = "private";
}

public sealed class IncomingSourceDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("purpose")]
    public string Purpose { get; set; } = "shared_intake";

    [JsonPropertyName("default_handling")]
    public string DefaultHandling { get; set; } = "route_automatically";

    [JsonPropertyName("include_subdirectories")]
    public bool IncludeSubdirectories { get; set; } = true;

    [JsonPropertyName("source_type")]
    public string SourceType { get; set; } = "local_folder";

    [JsonPropertyName("notes")]
    public string? Notes { get; set; }
}

public sealed class UpdateIncomingSourcesRequest
{
    [JsonPropertyName("incoming_sources")]
    public List<IncomingSourceDto> IncomingSources { get; init; } = [];
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
