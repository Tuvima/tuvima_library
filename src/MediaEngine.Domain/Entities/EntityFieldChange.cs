namespace MediaEngine.Domain.Entities;

/// <summary>
/// A single field-level change recorded as part of an <see cref="EntityEvent"/>.
/// Stored in the <c>entity_field_changes</c> table.
/// </summary>
public sealed class EntityFieldChange
{
    /// <summary>Row primary key.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>FK to the parent event.</summary>
    public Guid EventId { get; set; }

    /// <summary>The entity this change belongs to (denormalised for fast queries).</summary>
    public Guid EntityId { get; set; }

    /// <summary>Claim key / field name (e.g. "title", "author", "description", "headshot_url").</summary>
    public string Field { get; set; } = "";

    /// <summary>Previous canonical value. Null on first set.</summary>
    public string? OldValue { get; set; }

    /// <summary>New canonical value.</summary>
    public string? NewValue { get; set; }

    /// <summary>Provider UUID that supplied the old value.</summary>
    public string? OldProviderId { get; set; }

    /// <summary>Provider UUID that supplied the new value.</summary>
    public string? NewProviderId { get; set; }

    /// <summary>Field-level confidence after this change.</summary>
    public double? Confidence { get; set; }

    /// <summary>
    /// True when <see cref="OldValue"/> came from the original file's embedded metadata.
    /// Enables the "revert" feature — restoring the file to its pre-writeback state.
    /// </summary>
    public bool IsFileOriginal { get; set; }
}
