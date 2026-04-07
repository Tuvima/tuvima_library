namespace MediaEngine.Domain.Enums;

/// <summary>
/// Classifies a Work's role in the parent/child hierarchy introduced by
/// migration M-081.
///
/// <list type="bullet">
///   <item><see cref="Standalone"/> — A self-contained title with no
///     parent and no children. Default for everything in the library
///     until the HierarchyResolver promotes it. Examples: a one-off
///     movie, a single-volume novel.</item>
///   <item><see cref="Parent"/> — A container Work whose children are
///     the actual addressable items. Parents may themselves have a
///     parent (a Season is a child of a Show and a parent of Episodes).
///     Examples: a music album, a TV season, a comic series, a book
///     series.</item>
///   <item><see cref="Child"/> — A leaf Work that belongs to a
///     <see cref="Parent"/> via <c>parent_work_id</c>. Examples: a
///     track on an album, an episode in a season, an issue in a
///     comic series.</item>
///   <item><see cref="Catalog"/> — A Work known to exist (from
///     Wikidata or a retail provider) but not yet present in the
///     user's library. Catalog Works carry full metadata so the
///     album/season/series view can show what's missing, and are
///     promoted to <see cref="Standalone"/> or <see cref="Child"/>
///     when their files are eventually ingested.</item>
/// </list>
///
/// Stored as a TEXT column on <c>works.work_kind</c> with a
/// CHECK constraint enforcing the four values above.
/// </summary>
public enum WorkKind
{
    /// <summary>Default. Self-contained, no parent, no children.</summary>
    Standalone = 0,

    /// <summary>Container Work whose children are addressable.</summary>
    Parent = 1,

    /// <summary>Leaf Work that belongs to a <see cref="Parent"/>.</summary>
    Child = 2,

    /// <summary>Known to exist externally but not yet in the library.</summary>
    Catalog = 3,
}
