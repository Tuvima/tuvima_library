using System.Text.Json.Serialization;

namespace MediaEngine.Web.Models.ViewDTOs;

public sealed class MediaEditorContextDto
{
    [JsonPropertyName("launch_entity_id")]
    public Guid LaunchEntityId { get; set; }

    [JsonPropertyName("launch_entity_kind")]
    public string LaunchEntityKind { get; set; } = "Work";

    [JsonPropertyName("media_type")]
    public string MediaType { get; set; } = string.Empty;

    [JsonPropertyName("initial_scope")]
    public string InitialScope { get; set; } = string.Empty;

    [JsonPropertyName("scopes")]
    public List<MediaEditorScopeDto> Scopes { get; set; } = [];
}

public sealed class MediaEditorScopeDto
{
    [JsonPropertyName("scope_id")]
    public string ScopeId { get; set; } = string.Empty;

    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    [JsonPropertyName("order")]
    public int Order { get; set; }

    [JsonPropertyName("field_entity_id")]
    public Guid FieldEntityId { get; set; }

    [JsonPropertyName("field_entity_kind")]
    public string FieldEntityKind { get; set; } = "Work";

    [JsonPropertyName("artwork_owner_entity_id")]
    public Guid? ArtworkOwnerEntityId { get; set; }

    [JsonPropertyName("artwork_owner_entity_kind")]
    public string? ArtworkOwnerEntityKind { get; set; }

    [JsonPropertyName("display_title")]
    public string DisplayTitle { get; set; } = string.Empty;

    [JsonPropertyName("display_subtitle")]
    public string? DisplaySubtitle { get; set; }

    [JsonPropertyName("breadcrumb_label")]
    public string BreadcrumbLabel { get; set; } = string.Empty;

    [JsonPropertyName("canonical_target_group")]
    public string CanonicalTargetGroup { get; set; } = string.Empty;

    [JsonPropertyName("scope_summary")]
    public string? ScopeSummary { get; set; }

    [JsonPropertyName("read_only_hint")]
    public string? ReadOnlyHint { get; set; }

    [JsonPropertyName("can_edit_fields")]
    public bool CanEditFields { get; set; } = true;

    [JsonPropertyName("can_edit_artwork")]
    public bool CanEditArtwork { get; set; }
}
