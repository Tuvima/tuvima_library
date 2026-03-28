namespace MediaEngine.Web.Models.ViewDTOs;

// ── Universe Health ───────────────────────────────────────────────────────────

/// <summary>
/// Health score for a fictional universe: how well enriched the entity graph is.
/// Returned by GET /universe/{qid}/health.
/// </summary>
public sealed class UniverseHealthDto
{
    public string Qid                   { get; set; } = string.Empty;
    public string Label                 { get; set; } = string.Empty;
    public int    EntitiesTotal         { get; set; }
    public int    EntitiesEnriched      { get; set; }
    public int    EntitiesWithImages    { get; set; }
    public int    RelationshipsTotal    { get; set; }
    public double HealthPercent         { get; set; }
}

// ── Character Portraits ───────────────────────────────────────────────────────

/// <summary>
/// A portrait of a specific actor portraying a specific character.
/// Returned as part of character portrait lists in Vault drawer.
/// </summary>
public sealed class CharacterPortraitDto
{
    public Guid    Id                { get; set; }
    public Guid    PersonId          { get; set; }
    public string? PersonName        { get; set; }
    public Guid    FictionalEntityId { get; set; }
    public string? CharacterName     { get; set; }
    public string? ImageUrl          { get; set; }
    public bool    IsDefault         { get; set; }
}

// ── Character Roles (Person view) ─────────────────────────────────────────────

/// <summary>
/// A character role played by a specific person (actor), with their portrait for
/// that specific portrayal. Used in the Person detail drawer — Character Roles section.
/// </summary>
public sealed class CharacterRoleDto
{
    public Guid    FictionalEntityId { get; set; }
    public string? CharacterName     { get; set; }
    public string? PortraitUrl       { get; set; }
    public string? WorkTitle         { get; set; }
    public bool    IsDefault         { get; set; }
    public string? UniverseQid       { get; set; }
    public string? UniverseLabel     { get; set; }
}

// ── Universe Character (Universe view) ───────────────────────────────────────

/// <summary>
/// A fictional character within a universe, with the default actor and portrait.
/// Used in the Universe detail drawer — Characters section.
/// </summary>
public sealed class UniverseCharacterDto
{
    public Guid    FictionalEntityId { get; set; }
    public string  CharacterName     { get; set; } = string.Empty;
    public string? DefaultActorName  { get; set; }
    public Guid?   DefaultActorId    { get; set; }
    public string? PortraitUrl       { get; set; }
    public int     ActorCount        { get; set; }
}

// ── Entity Assets ─────────────────────────────────────────────────────────────

/// <summary>
/// A typed image asset belonging to any entity in the library.
/// Returned by GET /vault/assets/{entityId}.
/// </summary>
public sealed class EntityAssetDto
{
    public Guid    Id             { get; set; }
    public string  EntityId       { get; set; } = string.Empty;
    public string  AssetType      { get; set; } = string.Empty;
    public string? ImageUrl       { get; set; }
    public bool    IsPreferred    { get; set; }
    public string? SourceProvider { get; set; }
}
