namespace MediaEngine.Web.Models.ViewDTOs;

/// <summary>
/// Full person detail: headshot, biography, and social links.
/// Used by the library drawer. Full page person rendering now uses the unified
/// detail page at /details/person/{id}.
/// </summary>
public sealed class PersonDetailViewModel
{
    public Guid     Id               { get; init; }
    public string   Name             { get; init; } = string.Empty;
    public List<string> Roles        { get; init; } = [];
    public string?  HeadshotUrl      { get; init; }
    public bool     HasLocalHeadshot { get; init; }
    public string?  LocalHeadshotUrl { get; init; }
    public string?  Biography        { get; init; }
    public string?  Occupation       { get; init; }
    public string?  DateOfBirth      { get; init; }
    public string?  DateOfDeath      { get; init; }
    public string?  PlaceOfBirth     { get; init; }
    public string?  PlaceOfDeath     { get; init; }
    public string?  Nationality      { get; init; }
    public string?  WikidataQid      { get; init; }

    // Social links
    public string?  Instagram  { get; init; }
    public string?  Twitter    { get; init; }
    public string?  TikTok     { get; init; }
    public string?  Mastodon   { get; init; }
    public string?  Website    { get; init; }
    public bool     IsGroup    { get; init; }
    public List<GroupMemberView> GroupMembers { get; init; } = [];
    public List<GroupMemberView> MemberOfGroups { get; init; } = [];
    public string?  BannerUrl      { get; init; }
    public string?  BackgroundUrl  { get; init; }
    public string?  LogoUrl        { get; init; }

    // ── Helpers ──────────────────────────────────────────────────────────────

    public string EffectiveHeadshotUrl => LocalHeadshotUrl ?? string.Empty;

    public bool HasHeadshot => !string.IsNullOrEmpty(LocalHeadshotUrl);

    public string? HeaderArtUrl => BannerUrl ?? BackgroundUrl;

    public bool HasSocialLinks =>
        !string.IsNullOrEmpty(Instagram) ||
        !string.IsNullOrEmpty(Twitter)   ||
        !string.IsNullOrEmpty(TikTok)    ||
        !string.IsNullOrEmpty(Mastodon)  ||
        !string.IsNullOrEmpty(Website);

    public string Initials
    {
        get
        {
            var parts = Name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length switch
            {
                0 => "?",
                1 => parts[0][..1].ToUpperInvariant(),
                _ => $"{parts[0][0]}{parts[^1][0]}".ToUpperInvariant(),
            };
        }
    }

    /// <summary>Formatted role labels.</summary>
    public List<string> RoleLabels => Roles.Select(r => r.ToLowerInvariant() switch
    {
        "author"      => "Author",
        "narrator"    => "Narrator",
        "director"    => "Director",
        "actor"       => "Actor",
        "voice actor" => "Voice Actor",
        "artist"      => "Artist",
        "performer"   => "Performer",
        "illustrator" => "Illustrator",
        "composer"    => "Composer",
        _             => r,
    }).ToList();

    /// <summary>Primary role label for backward-compatible display.</summary>
    public string RoleLabel => RoleLabels.FirstOrDefault() ?? string.Empty;
}
