namespace MediaEngine.Domain.Models;

/// <summary>
/// Recommended watching/reading order for a franchise or series.
/// </summary>
public sealed class WatchingOrder
{
    /// <summary>The Hub or Parent Hub this order applies to.</summary>
    public Guid HubId { get; init; }

    /// <summary>Order type (e.g. "publication", "chronological", "recommended").</summary>
    public required string OrderType { get; init; }

    /// <summary>Ordered list of works in this recommended sequence.</summary>
    public required IReadOnlyList<WatchingOrderEntry> Entries { get; init; }

    /// <summary>LLM-generated explanation of why this order is recommended.</summary>
    public string? Explanation { get; init; }
}

/// <summary>
/// A single entry in a watching/reading order.
/// </summary>
public sealed class WatchingOrderEntry
{
    /// <summary>Position in the order (1-based).</summary>
    public int Position { get; init; }

    /// <summary>Work entity ID.</summary>
    public Guid WorkId { get; init; }

    /// <summary>Work title for display.</summary>
    public required string Title { get; init; }

    /// <summary>Optional note about this entry (e.g. "Optional — backstory context").</summary>
    public string? Note { get; init; }
}
