using MediaEngine.Domain.Enums;

namespace MediaEngine.Domain.Contracts;

/// <summary>
/// Repository for querying and persisting Work entities.
///
/// As of Phase 3 (M-082), the interface centres on parent/child resolution
/// rather than ad-hoc title+author dedup. The legacy
/// <c>FindByTitleAuthorAsync</c> path is gone — hierarchical media types
/// (music, TV, comics, series-bound books) flow through the
/// <c>HierarchyResolver</c>, which calls <see cref="FindParentByKeyAsync"/>
/// and <see cref="FindChildByOrdinalAsync"/>.
/// </summary>
public interface IWorkRepository
{
    /// <summary>
    /// Finds a parent Work for the given media type and normalized
    /// <paramref name="parentKey"/>. Used as the indexed find-or-create
    /// lookup at the start of every hierarchical ingestion.
    /// Returns null when no parent exists yet.
    /// </summary>
    Task<Guid?> FindParentByKeyAsync(
        MediaType mediaType,
        string parentKey,
        CancellationToken ct = default);

    /// <summary>
    /// Finds a child Work of the given parent at the given ordinal
    /// (track number, episode number, issue number, volume number).
    /// Returns null when no child exists at that position.
    /// </summary>
    Task<Guid?> FindChildByOrdinalAsync(
        Guid parentWorkId,
        int ordinal,
        CancellationToken ct = default);

    /// <summary>
    /// Finds a child Work of the given parent by case-insensitive title
    /// match. Used as a fallback when the source file lacks an ordinal
    /// (track number missing, episode title only, etc.).
    /// </summary>
    Task<Guid?> FindChildByTitleAsync(
        Guid parentWorkId,
        string title,
        CancellationToken ct = default);

    /// <summary>
    /// Finds any Work whose <c>external_identifiers</c> JSON blob contains
    /// the given <paramref name="scheme"/> → <paramref name="value"/> pair.
    /// Used by the resolver to promote catalog rows when their files arrive.
    /// </summary>
    Task<Guid?> FindByExternalIdentifierAsync(
        string scheme,
        string value,
        CancellationToken ct = default);

    /// <summary>
    /// Inserts a new parent Work. Sets <c>work_kind = 'parent'</c> and
    /// records the <paramref name="parentKey"/> for future find-or-create
    /// hits. Returns the new Work's id.
    /// </summary>
    Task<Guid> InsertParentAsync(
        MediaType mediaType,
        string parentKey,
        Guid? grandparentWorkId,
        int? ordinal,
        CancellationToken ct = default);

    /// <summary>
    /// Inserts a new child Work. Sets <c>work_kind = 'child'</c> and
    /// links to <paramref name="parentWorkId"/>. Returns the new Work's id.
    /// </summary>
    Task<Guid> InsertChildAsync(
        MediaType mediaType,
        Guid parentWorkId,
        int? ordinal,
        CancellationToken ct = default);

    /// <summary>
    /// Inserts a new standalone Work (movies, single-title books, etc.).
    /// Returns the new Work's id.
    /// </summary>
    Task<Guid> InsertStandaloneAsync(
        MediaType mediaType,
        CancellationToken ct = default);

    /// <summary>
    /// Inserts a new catalog Work — known externally but no file in the
    /// library yet. <paramref name="ordinal"/> is the child's position
    /// inside its parent. The CatalogUpsertService uses this when Wikidata
    /// reveals tracks/episodes/issues you don't yet own.
    /// </summary>
    Task<Guid> InsertCatalogChildAsync(
        MediaType mediaType,
        Guid parentWorkId,
        int? ordinal,
        IReadOnlyDictionary<string, string>? externalIdentifiers,
        CancellationToken ct = default);

    /// <summary>
    /// Promotes a catalog row (<c>work_kind = 'catalog'</c>,
    /// <c>is_catalog_only = 1</c>) to a real owned child Work
    /// (<c>work_kind = 'child'</c>, <c>is_catalog_only = 0</c>) once a
    /// matching file is ingested.
    /// </summary>
    Task PromoteCatalogToOwnedAsync(Guid workId, CancellationToken ct = default);

    /// <summary>
    /// Merges the given identifiers into <c>works.external_identifiers</c>.
    /// Existing keys are NOT overwritten — only missing keys are added.
    /// Each pipeline layer (file processor → retail → Wikidata) calls this
    /// with the IDs it knows about.
    /// </summary>
    Task WriteExternalIdentifiersAsync(
        Guid workId,
        IReadOnlyDictionary<string, string> identifiers,
        CancellationToken ct = default);
}
