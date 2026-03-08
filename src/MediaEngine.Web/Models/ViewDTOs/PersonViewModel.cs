namespace MediaEngine.Web.Models.ViewDTOs;

/// <summary>
/// Dashboard view model for a person (author, narrator, director, etc.)
/// linked to media assets in the library.
/// </summary>
public sealed class PersonViewModel
{
    public Guid    Id              { get; init; }
    public string  Name            { get; init; } = string.Empty;
    public string  Role            { get; init; } = string.Empty;
    public string? HeadshotUrl     { get; init; }
    public bool    HasLocalHeadshot { get; init; }
    public string? Biography       { get; init; }
    public string? Occupation      { get; init; }

    // ── Display helpers ─────────────────────────────────────────────────

    /// <summary>Up to two initials for fallback avatar (e.g. "JR" for "J.R.R. Tolkien").</summary>
    public string Initials
    {
        get
        {
            var parts = Name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length switch
            {
                0 => "?",
                1 => parts[0][..1].ToUpperInvariant(),
                _ => $"{parts[0][..1]}{parts[^1][..1]}".ToUpperInvariant(),
            };
        }
    }

    /// <summary>Formatted role label (e.g. "Author" → "Author").</summary>
    public string RoleLabel => Role switch
    {
        "Author"       => "Author",
        "Narrator"     => "Narrator",
        "Director"     => "Director",
        "Illustrator"  => "Illustrator",
        "Cast Member"  => "Cast",
        "Voice Actor"  => "Voice Actor",
        "Screenwriter" => "Screenwriter",
        "Composer"     => "Composer",
        _              => Role,
    };
}
