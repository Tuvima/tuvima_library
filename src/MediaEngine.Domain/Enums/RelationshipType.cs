namespace MediaEngine.Domain.Enums;

/// <summary>
/// String constants for relationship types stored in the <c>entity_relationships</c> table.
/// Each constant maps to a Wikidata property that defines a graph edge between
/// two entities in the universe graph.
/// </summary>
public static class RelationshipType
{
    // ── Character → Character ────────────────────────────────────────────

    /// <summary>P22 — Father relationship (parent → child).</summary>
    public const string Father = "father";

    /// <summary>P25 — Mother relationship (parent → child).</summary>
    public const string Mother = "mother";

    /// <summary>P26 — Spouse/partner relationship.</summary>
    public const string Spouse = "spouse";

    /// <summary>P3373 — Sibling relationship.</summary>
    public const string Sibling = "sibling";

    /// <summary>P40 — Child relationship (parent → child).</summary>
    public const string Child = "child";

    /// <summary>P1344 — Enemy or rival relationship.</summary>
    public const string Opponent = "opponent";

    /// <summary>P1066 — Student-of / mentor relationship.</summary>
    public const string StudentOf = "student_of";

    // ── Character/Location → Organization ────────────────────────────────

    /// <summary>P463 — Organization membership.</summary>
    public const string MemberOf = "member_of";

    // ── Character/Entity → Location ──────────────────────────────────────

    /// <summary>P551 — Where a character resides.</summary>
    public const string Residence = "residence";

    /// <summary>P131 — Located in the administrative territorial entity.</summary>
    public const string LocatedIn = "located_in";

    /// <summary>P361 — Part-of relationship (sub-location or sub-organization).</summary>
    public const string PartOf = "part_of";

    // ── Organization ─────────────────────────────────────────────────────

    /// <summary>P169 — Head of organization.</summary>
    public const string HeadOf = "head_of";

    /// <summary>P749 — Parent organization.</summary>
    public const string ParentOrganization = "parent_organization";

    /// <summary>P527 — Organization has parts/members.</summary>
    public const string HasParts = "has_parts";

    // ── Cross-type ───────────────────────────────────────────────────────

    /// <summary>P170 — Creator of a fictional character.</summary>
    public const string Creator = "creator";

    /// <summary>P175 — Performer (actor who portrays a character).</summary>
    public const string Performer = "performer";

    /// <summary>Two entities with different QIDs that represent the same concept.</summary>
    public const string SameAs = "same_as";
}
