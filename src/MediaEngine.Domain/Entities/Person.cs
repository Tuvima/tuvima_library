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
    /// The roles this person plays across associated media assets.
    /// Populated from the <c>person_roles</c> junction table.
    /// Examples: "Author", "Director", "Actor".
    /// </summary>
    public List<string> Roles { get; set; } = [];

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

    // ── Biographical fields (Phase 12) ─────────────────────────────────────

    /// <summary>Date of birth from Wikidata P569 (e.g. "1920-01-02").</summary>
    public string? DateOfBirth { get; set; }

    /// <summary>Date of death from Wikidata P570 (e.g. "1986-02-11").</summary>
    public string? DateOfDeath { get; set; }

    /// <summary>Place of birth from Wikidata P19 (e.g. "Tacoma, Washington").</summary>
    public string? PlaceOfBirth { get; set; }

    /// <summary>Place of death from Wikidata P20.</summary>
    public string? PlaceOfDeath { get; set; }

    /// <summary>Country of citizenship from Wikidata P27 (e.g. "American").</summary>
    public string? Nationality { get; set; }

    /// <summary>
    /// Whether this person record represents a pseudonym (pen name / stage name).
    /// When <c>true</c>, the <c>person_aliases</c> table links this record to the
    /// real person(s) behind the pen name.
    /// </summary>
    public bool IsPseudonym { get; set; }

    /// <summary>
    /// Whether this person record represents a musical group, band, or ensemble
    /// rather than an individual. Stored but member expansion is not performed —
    /// the group is stored as a single Person record.
    /// Determined by Wikidata P31 = Q215380 (musical group) or Q5741069 (musical ensemble).
    /// </summary>
    public bool IsGroup { get; set; }

    /// <summary>
    /// Path to the locally downloaded headshot image under
    /// <c>{LibraryRoot}/.people/{id}/headshot.jpg</c>.
    /// Null until the headshot has been downloaded from Wikimedia Commons.
    /// </summary>
    public string? LocalHeadshotPath { get; set; }

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

    /// <summary>
    /// The Wikidata entity revision ID from the last enrichment.
    /// Used for lightweight freshness checks: if the revision hasn't changed,
    /// skip the full property re-fetch during the 30-day refresh cycle.
    /// <c>null</c> means no revision has been recorded (pre-M-065 records).
    /// Maps to <c>persons.last_revision_id</c> (INTEGER in SQLite).
    /// </summary>
    public long? LastRevisionId { get; set; }
}
