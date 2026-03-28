namespace MediaEngine.Domain.Entities;

/// <summary>
/// An image of a specific performer portraying a specific fictional character —
/// actor-in-costume for live-action, or animated character artwork for animation.
///
/// Links a <see cref="Person"/> (the performer) to a <see cref="FictionalEntity"/>
/// (the character) with visual evidence. Multiple portraits may exist for the same
/// character (different actors across adaptations). One is marked as the default.
///
/// Maps 1:1 to a row in the <c>character_portraits</c> table.
/// </summary>
public sealed class CharacterPortrait
{
    /// <summary>Stable row identifier (UUID → TEXT in SQLite).</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// The real person (actor/voice actor) in this portrait.
    /// References <c>persons.id</c>.
    /// </summary>
    public Guid PersonId { get; set; }

    /// <summary>
    /// The fictional character depicted.
    /// References <c>fictional_entities.id</c>.
    /// </summary>
    public Guid FictionalEntityId { get; set; }

    /// <summary>Remote URL of the portrait image (e.g. Fanart.tv CDN).</summary>
    public string? ImageUrl { get; set; }

    /// <summary>Local filesystem path to the downloaded portrait.</summary>
    public string? LocalImagePath { get; set; }

    /// <summary>
    /// Which provider supplied this portrait.
    /// Example: <c>"fanart_tv"</c>, <c>"wikidata"</c>, <c>"user_upload"</c>.
    /// </summary>
    public string? SourceProvider { get; set; }

    /// <summary>
    /// Whether this is the default portrait for the character.
    /// Only one portrait per character should be marked as default.
    /// </summary>
    public bool IsDefault { get; set; }

    /// <summary>When this portrait record was created.</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>When this portrait was last updated.</summary>
    public DateTimeOffset? UpdatedAt { get; set; }
}
