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
    public string? Instagram { get; set; }
    public string? Twitter { get; set; }
    public string? TikTok { get; set; }
    public string? Mastodon { get; set; }
    public string? Website { get; set; }
}
