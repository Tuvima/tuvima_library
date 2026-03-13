namespace MediaEngine.Domain.Enums;

/// <summary>
/// Discriminator for the unified <c>fictional_entities</c> table.
/// Each fictional entity belongs to exactly one sub-type.
/// </summary>
public static class FictionalEntityType
{
    /// <summary>A fictional character (e.g. Paul Atreides, Sherlock Holmes).</summary>
    public const string Character = "Character";

    /// <summary>A fictional location (e.g. Arrakis, Middle-earth, Gotham City).</summary>
    public const string Location = "Location";

    /// <summary>A fictional organization or faction (e.g. House Atreides, S.H.I.E.L.D.).</summary>
    public const string Organization = "Organization";

    /// <summary>All valid sub-type values for CHECK constraint generation.</summary>
    public static readonly IReadOnlyList<string> All = [Character, Location, Organization];
}
