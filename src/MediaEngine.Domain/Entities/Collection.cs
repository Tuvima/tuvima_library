namespace MediaEngine.Domain.Entities;

/// <summary>
/// A user-defined or system-generated grouping of Works.
/// Examples: "Reading List", "Watch Later", custom collections.
/// Separate from Hubs — Collections are user-curated, Hubs are intelligence-driven.
///
/// Stored in the <c>collections</c> table (schema stub — no repository or API this sprint).
/// </summary>
public sealed class Collection
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>"reading_list", "watch_list", "custom".</summary>
    public string CollectionType { get; set; } = "custom";

    /// <summary>Null = shared across profiles; non-null = per-user.</summary>
    public Guid? ProfileId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
