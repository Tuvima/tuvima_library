namespace MediaEngine.Domain.Entities;

/// <summary>
/// Junction entity linking a <see cref="Collection"/> to a Work.
///
/// Stored in the <c>collection_items</c> table (schema stub — no repository or API this sprint).
/// </summary>
public sealed class CollectionItem
{
    public Guid Id { get; set; }
    public Guid CollectionId { get; set; }
    public Guid WorkId { get; set; }
    public int SortOrder { get; set; }
    public DateTimeOffset AddedAt { get; set; }
}
