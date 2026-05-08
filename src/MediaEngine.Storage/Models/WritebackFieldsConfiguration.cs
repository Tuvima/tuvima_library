using System.Text.Json.Serialization;

namespace MediaEngine.Storage.Models;

/// <summary>
/// Per-media-type list of writable claim keys.
/// Loaded from <c>config/writeback-fields.json</c>.
/// Single source of truth for both file write-back and library detail display.
/// </summary>
public sealed class WritebackFieldsConfiguration
{
    [JsonPropertyName("Books")]
    public List<string> Books { get; set; } = [];

    [JsonPropertyName("Audiobooks")]
    public List<string> Audiobooks { get; set; } = [];

    [JsonPropertyName("Movies")]
    public List<string> Movies { get; set; } = [];

    [JsonPropertyName("TV")]
    public List<string> TV { get; set; } = [];

    [JsonPropertyName("Music")]
    public List<string> Music { get; set; } = [];

    [JsonPropertyName("Comics")]
    public List<string> Comics { get; set; } = [];

    /// <summary>Returns the writable field list for the given media type, or an empty list if unknown.</summary>
    public IReadOnlyList<string> GetFieldsFor(string? mediaType) => (mediaType ?? "") switch
    {
        "Books" => Books,
        "Book" => Books,
        "Audiobooks" => Audiobooks,
        "Audiobook" => Audiobooks,
        "Movies" => Movies,
        "Movie" => Movies,
        "TV" => TV,
        "Music" => Music,

        "Comics" => Comics,
        "Comic" => Comics,
        _ => []
    };
}
