using System.Text.Json.Serialization;

namespace MediaEngine.Web.Models.ViewDTOs;

/// <summary>
/// Dashboard view model for a managed hub (Smart, System, Mix, Playlist)
/// displayed in the Vault Hubs tab.
/// </summary>
public sealed class ManagedHubViewModel
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("icon_name")]
    public string? IconName { get; set; }

    [JsonPropertyName("hub_type")]
    public string HubType { get; set; } = "Smart";

    [JsonPropertyName("scope")]
    public string Scope { get; set; } = "library";

    [JsonPropertyName("profile_id")]
    public Guid? ProfileId { get; set; }

    [JsonPropertyName("is_enabled")]
    public bool IsEnabled { get; set; } = true;

    [JsonPropertyName("is_featured")]
    public bool IsFeatured { get; set; }

    [JsonPropertyName("min_items")]
    public int MinItems { get; set; }

    [JsonPropertyName("rule_json")]
    public string? RuleJson { get; set; }

    [JsonPropertyName("refresh_schedule")]
    public string? RefreshSchedule { get; set; }

    [JsonPropertyName("item_count")]
    public int ItemCount { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = "Active";

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonPropertyName("modified_at")]
    public DateTimeOffset? ModifiedAt { get; set; }

    // ── Computed display helpers ──

    public string TypeColor => HubType switch
    {
        "Smart"    => "#60A5FA",
        "System"   => "#5DCAA5",
        "Mix"      => "#A78BFA",
        "Playlist" => "#C9922E",
        _          => "#60A5FA",
    };

    public string TypeLabel => HubType switch
    {
        "Smart"    => "Smart",
        "System"   => "System",
        "Mix"      => "Mix",
        "Playlist" => "Playlist",
        _          => HubType,
    };

    public string StatusColor => Status switch
    {
        "Active"   => "#5DCAA5",
        "Disabled" => "rgba(255,255,255,0.4)",
        "Empty"    => "#EF9F27",
        _          => "#5DCAA5",
    };

    public string StatusLabel => Status switch
    {
        "Active"   => "Active",
        "Disabled" => "Disabled",
        "Empty"    => "Empty",
        _          => Status,
    };
}
