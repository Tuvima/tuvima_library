using System.Text.Json.Serialization;
using MediaEngine.Domain.Aggregates;

namespace MediaEngine.Api.Models;

/// <summary>
/// DTO for non-Universe hubs displayed in the Vault Hubs tab.
/// Includes management fields (enabled, featured, rules) not present in <see cref="HubDto"/>.
/// </summary>
public sealed class ManagedHubDto
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("icon_name")]
    public string? IconName { get; init; }

    [JsonPropertyName("hub_type")]
    public string HubType { get; init; } = "Smart";

    [JsonPropertyName("scope")]
    public string Scope { get; init; } = "library";

    [JsonPropertyName("profile_id")]
    public Guid? ProfileId { get; init; }

    [JsonPropertyName("is_enabled")]
    public bool IsEnabled { get; init; } = true;

    [JsonPropertyName("is_featured")]
    public bool IsFeatured { get; init; }

    [JsonPropertyName("min_items")]
    public int MinItems { get; init; }

    [JsonPropertyName("rule_json")]
    public string? RuleJson { get; init; }

    [JsonPropertyName("resolution")]
    public string Resolution { get; init; } = "query";

    [JsonPropertyName("rule_hash")]
    public string? RuleHash { get; init; }

    [JsonPropertyName("match_mode")]
    public string MatchMode { get; init; } = "all";

    [JsonPropertyName("sort_field")]
    public string? SortField { get; init; }

    [JsonPropertyName("sort_direction")]
    public string SortDirection { get; init; } = "desc";

    [JsonPropertyName("live_updating")]
    public bool LiveUpdating { get; init; } = true;

    [JsonPropertyName("refresh_schedule")]
    public string? RefreshSchedule { get; init; }

    [JsonPropertyName("item_count")]
    public int ItemCount { get; init; }

    [JsonPropertyName("status")]
    public string Status { get; init; } = "Active";

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; init; }

    [JsonPropertyName("modified_at")]
    public DateTimeOffset? ModifiedAt { get; init; }

    public static ManagedHubDto FromDomain(Hub hub, int itemCount) => new()
    {
        Id              = hub.Id,
        Name            = hub.DisplayName ?? $"Hub {hub.Id.ToString("N")[..8]}",
        Description     = hub.Description,
        IconName        = hub.IconName,
        HubType         = hub.HubType,
        Scope           = hub.Scope,
        ProfileId       = hub.ProfileId,
        IsEnabled       = hub.IsEnabled,
        IsFeatured      = hub.IsFeatured,
        MinItems        = hub.MinItems,
        RuleJson        = hub.RuleJson,
        Resolution      = hub.Resolution,
        RuleHash        = hub.RuleHash,
        MatchMode       = hub.MatchMode,
        SortField       = hub.SortField,
        SortDirection   = hub.SortDirection,
        LiveUpdating    = hub.LiveUpdating,
        RefreshSchedule = hub.RefreshSchedule,
        ItemCount       = itemCount,
        Status          = !hub.IsEnabled ? "Disabled" : itemCount == 0 ? "Empty" : "Active",
        CreatedAt       = hub.CreatedAt,
        ModifiedAt      = hub.ModifiedAt,
    };
}
