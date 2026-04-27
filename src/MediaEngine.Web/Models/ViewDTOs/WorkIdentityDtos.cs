using System.Text.Json.Serialization;

namespace MediaEngine.Web.Models.ViewDTOs;

public sealed class WorkDetailViewModel
{
    [JsonPropertyName("id")] public Guid Id { get; init; }
    [JsonPropertyName("collection_id")] public Guid? CollectionId { get; init; }
    [JsonPropertyName("parent_work_id")] public Guid? ParentWorkId { get; init; }
    [JsonPropertyName("media_type")] public string MediaType { get; init; } = string.Empty;
    [JsonPropertyName("work_kind")] public string WorkKind { get; init; } = string.Empty;
    [JsonPropertyName("ordinal")] public int? Ordinal { get; init; }
    [JsonPropertyName("is_catalog_only")] public bool IsCatalogOnly { get; init; }
    [JsonPropertyName("wikidata_qid")] public string? WikidataQid { get; init; }
    [JsonPropertyName("canonical_values")] public List<CanonicalValueViewModel> CanonicalValues { get; init; } = [];
    [JsonPropertyName("editions")] public List<EditionViewModel> Editions { get; init; } = [];
}

public sealed class EditionViewModel
{
    [JsonPropertyName("id")] public Guid Id { get; init; }
    [JsonPropertyName("work_id")] public Guid WorkId { get; init; }
    [JsonPropertyName("format_label")] public string? FormatLabel { get; init; }
    [JsonPropertyName("wikidata_qid")] public string? WikidataQid { get; init; }
    [JsonPropertyName("canonical_values")] public List<CanonicalValueViewModel> CanonicalValues { get; init; } = [];
    [JsonPropertyName("assets")] public List<EditionAssetViewModel> Assets { get; init; } = [];
}

public sealed class EditionAssetViewModel
{
    [JsonPropertyName("id")] public Guid Id { get; init; }
    [JsonPropertyName("edition_id")] public Guid EditionId { get; init; }
    [JsonPropertyName("file_path_root")] public string FilePathRoot { get; init; } = string.Empty;
    [JsonPropertyName("status")] public string Status { get; init; } = string.Empty;
    [JsonPropertyName("canonical_values")] public List<CanonicalValueViewModel> CanonicalValues { get; init; } = [];
}
