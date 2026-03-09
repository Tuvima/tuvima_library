namespace MediaEngine.Web.Models.ViewDTOs;

/// <summary>
/// Full person detail: headshot, biography, and social links.
/// Used by PersonDetail.razor at /person/{id}.
/// </summary>
public sealed class PersonDetailViewModel
{
    public Guid     Id               { get; init; }
    public string   Name             { get; init; } = string.Empty;
    public string   Role             { get; init; } = string.Empty;
    public string?  HeadshotUrl      { get; init; }
    public bool     HasLocalHeadshot { get; init; }
    public string?  LocalHeadshotUrl { get; init; }
    public string?  Biography        { get; init; }
    public string?  Occupation       { get; init; }

    // Social links
    public string?  Instagram  { get; init; }
    public string?  Twitter    { get; init; }
    public string?  TikTok     { get; init; }
    public string?  Mastodon   { get; init; }
    public string?  Website    { get; init; }

    // ── Helpers ──────────────────────────────────────────────────────────────

    public string EffectiveHeadshotUrl => LocalHeadshotUrl ?? HeadshotUrl ?? string.Empty;

    public bool HasHeadshot => !string.IsNullOrEmpty(LocalHeadshotUrl) || !string.IsNullOrEmpty(HeadshotUrl);

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

    public string RoleLabel => Role.ToLowerInvariant() switch
    {
        "author"      => "Author",
        "narrator"    => "Narrator",
        "director"    => "Director",
        "illustrator" => "Illustrator",
        "composer"    => "Composer",
        _             => Role,
    };
}
