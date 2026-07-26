using MediaEngine.Contracts.Search;

namespace MediaEngine.Web.Models.ViewDTOs;

/// <summary>
/// Retail-search wire data plus presentation-only scoring details used by the provider tester.
/// The Engine does not currently emit description field matches, so those values remain empty.
/// </summary>
public sealed class RetailSearchCandidateViewModel
{
    public string ProviderId { get; init; } = string.Empty;
    public string ProviderName { get; init; } = string.Empty;
    public string? ProviderItemId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? Year { get; init; }
    public string? Author { get; init; }
    public string? Director { get; init; }
    public string? Description { get; init; }
    public string? CoverUrl { get; init; }
    public double Confidence { get; init; }
    public Dictionary<string, string> ExtraFields { get; init; } = [];
    public FieldMatchScoresDto? MatchScores { get; init; }
    public double CompositeScore { get; init; }

    public double DescriptionMatchScore { get; init; }
    public IReadOnlyList<DescriptionFieldMatchDto> DescriptionFieldMatches { get; init; } = [];

    public static RetailSearchCandidateViewModel FromContract(SearchRetailCandidateDto candidate) =>
        new()
        {
            ProviderId = candidate.ProviderId,
            ProviderName = candidate.ProviderName,
            ProviderItemId = candidate.ProviderItemId,
            Title = candidate.Title,
            Year = candidate.Year,
            Author = candidate.Author,
            Director = candidate.Director,
            Description = candidate.Description,
            CoverUrl = candidate.CoverUrl,
            Confidence = candidate.Confidence,
            ExtraFields = candidate.ExtraFields,
            MatchScores = candidate.MatchScores,
            CompositeScore = candidate.CompositeScore,
        };
}
