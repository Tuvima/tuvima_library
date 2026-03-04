namespace MediaEngine.Domain.Entities;

/// <summary>
/// An author, narrator, or other creative person linked to one or more media assets.
///
/// Persons are created when the ingestion engine extracts author/narrator names from
/// file metadata, and enriched asynchronously via the Wikidata adapter (Phase 9).
///
/// Maps 1:1 to a row in the <c>persons</c> table.
/// Spec: Phase 9 – Recursive Person Enrichment.
/// </summary>
public sealed class Person
{
    /// <summary>Stable row identifier (UUID → TEXT in SQLite).</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// The person's display name as extracted from file metadata.
    /// Example: <c>"Frank Herbert"</c>.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The role this person plays in associated media assets.
    /// Valid values: <c>"Author"</c>, <c>"Narrator"</c>, <c>"Director"</c>,
    /// <c>"Illustrator"</c>, <c>"Cast Member"</c>, <c>"Voice Actor"</c>,
    /// <c>"Screenwriter"</c>, <c>"Composer"</c>.
    /// Enforced by a CHECK constraint in the <c>persons</c> table.
    /// </summary>
    public string Role { get; set; } = string.Empty;

    /// <summary>
    /// The Wikidata Q-identifier for this person, if enriched.
    /// Example: <c>"Q42"</c> (Douglas Adams).
    /// Null until the Wikidata adapter has processed this person.
    /// </summary>
    public string? WikidataQid { get; set; }

    /// <summary>
    /// A URL to a headshot / portrait image for this person.
    /// Sourced from Wikimedia Commons via Wikidata P18 (image) claim.
    /// Null until enriched.
    /// </summary>
    public string? HeadshotUrl { get; set; }

    /// <summary>
    /// A short biography extracted from the Wikidata entity description.
    /// Null until enriched.
    /// </summary>
    public string? Biography { get; set; }

    /// <summary>
    /// The person's occupation as returned by Wikidata P106 (e.g. "Writer", "Actor").
    /// Used to filter person lookups by role type.
    /// Null until enriched.
    /// </summary>
    public string? Occupation { get; set; }

    /// <summary>
    /// Instagram handle or profile URL from Wikidata P2003.
    /// Part of the Social Pivot — direct links to official creator feeds.
    /// Null until enriched.
    /// </summary>
    public string? Instagram { get; set; }

    /// <summary>
    /// Twitter/X handle or profile URL from Wikidata P2002.
    /// Part of the Social Pivot — direct links to official creator feeds.
    /// Null until enriched.
    /// </summary>
    public string? Twitter { get; set; }

    /// <summary>
    /// TikTok handle or profile URL from Wikidata P7085.
    /// Part of the Social Pivot — links to modern video platforms.
    /// Null until enriched.
    /// </summary>
    public string? TikTok { get; set; }

    /// <summary>
    /// Mastodon address from Wikidata P4033.
    /// Part of the Social Pivot — links to decentralised platforms.
    /// Null until enriched.
    /// </summary>
    public string? Mastodon { get; set; }

    /// <summary>
    /// Official website URL from Wikidata P856.
    /// The primary digital home for creators and studios.
    /// Null until enriched.
    /// </summary>
    public string? Website { get; set; }

    /// <summary>
    /// When this person record was first created.
    /// Defaults to <see cref="DateTimeOffset.UtcNow"/> at construction time.
    /// Maps to <c>persons.created_at</c> (ISO-8601 TEXT in SQLite).
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// When this person was last enriched from an external provider.
    /// <c>null</c> means the person has not yet been enriched.
    /// The <see cref="RecursiveIdentityService"/> uses this to decide whether
    /// to enqueue a Wikidata harvest request.
    /// Maps to <c>persons.enriched_at</c> (ISO-8601 TEXT in SQLite).
    /// </summary>
    public DateTimeOffset? EnrichedAt { get; set; }
}
