namespace MediaEngine.Web.Models.ViewDTOs;

/// <summary>
/// Template variables available for phrase substitution.
/// The service replaces {PropertyName} tokens in templates with these values.
/// </summary>
public sealed record PhraseContext
{
    public Guid? EntityId { get; init; }
    public string? Title { get; init; }
    public string? Author { get; init; }
    public string? Series { get; init; }
    public string? Genre { get; init; }
    public string? MediaType { get; init; }
    public int? Count { get; init; }
    public double? ProgressPct { get; init; }
}
