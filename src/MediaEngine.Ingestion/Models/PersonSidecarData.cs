namespace MediaEngine.Ingestion.Models;

/// <summary>
/// Data carrier for person sidecar XML files stored under
/// <c>{LibraryRoot}/.people/{person-guid}/person.xml</c>.
///
/// The person sidecar provides filesystem portability — if the database
/// is wiped, person metadata can be rebuilt from these files.
/// </summary>
public sealed class PersonSidecarData
{
    public string Name { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string? WikidataQid { get; set; }
    public string? Biography { get; set; }
    public string? Occupation { get; set; }
    public bool IsPseudonym { get; set; }
    public string? Instagram { get; set; }
    public string? Twitter { get; set; }
    public string? TikTok { get; set; }
    public string? Mastodon { get; set; }
    public string? Website { get; set; }

    /// <summary>
    /// Pseudonym cross-references: persons who are pen names for this person.
    /// Each entry is (QID, display name).
    /// </summary>
    public IReadOnlyList<PersonSidecarRef> Pseudonyms { get; set; } = [];

    /// <summary>
    /// Real identity cross-references: persons whom this pseudonym points to.
    /// </summary>
    public IReadOnlyList<PersonSidecarRef> RealIdentities { get; set; } = [];
}

/// <summary>
/// A cross-reference to another person in a sidecar, identified by QID and name.
/// </summary>
public sealed class PersonSidecarRef
{
    public string Qid { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
}
