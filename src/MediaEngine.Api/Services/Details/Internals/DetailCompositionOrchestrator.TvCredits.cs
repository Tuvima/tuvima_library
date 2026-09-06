using MediaEngine.Contracts.Details;
using MediaEngine.Storage;
using MediaEngine.Api.Endpoints;
namespace MediaEngine.Api.Services.Details.Internals;

internal sealed partial class DetailCompositionOrchestrator
{
    private async Task<IReadOnlyList<CreditGroupViewModel>> BuildTvCreditsAsync(Guid id, CancellationToken ct)
    {
        var episodes = await new TvEpisodeCreditRepository(_db).ReadAsync(id, ct);
        var people = (await _persons.FindByNamesAsync(episodes.SelectMany(e => e.Credits).Select(c => c.Name).Distinct(), ct))
            .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() == 1)
            .ToDictionary(g => g.Key, g => g.Single(), StringComparer.OrdinalIgnoreCase);
        var credits = episodes.SelectMany(e => e.Credits.Select(c => (Episode: e, Credit: c)))
            .GroupBy(x => (x.Credit.PersonId, x.Credit.Job, x.Credit.Character))
            .Select(group =>
            {
                var credit = group.First().Credit;
                people.TryGetValue(credit.Name, out var person);
                return new EntityCreditViewModel
                {
                    EntityId = person?.Id.ToString("D") ?? $"tmdb:{credit.PersonId}",
                    EntityType = RelatedEntityType.Person,
                    DisplayName = credit.Name,
                    PrimaryRole = credit.Job,
                    CharacterName = credit.Character,
                    ImageUrl = person is not null && !string.IsNullOrWhiteSpace(person.LocalHeadshotPath)
                        ? ApiImageUrls.BuildPersonHeadshotUrl(person.Id, person.LocalHeadshotPath, null) : null,
                    SortOrder = credit.Order,
                    FallbackInitials = Initials(credit.Name),
                    SourceName = "TMDB",
                    SourceId = credit.PersonId,
                    Seasons = group.Select(x => x.Episode.Season).Distinct().Order().ToList(),
                };
            }).ToList();
        return credits.GroupBy(c => TvCreditGroup(c.PrimaryRole)).Select(group => new CreditGroupViewModel
        {
            GroupType = group.Key,
            Title = group.Key == CreditGroupType.Cast ? "Cast" : group.Key == CreditGroupType.CreativeTeam ? "Crew" : group.Key.ToString(),
            Credits = group.OrderBy(c => c.SortOrder).ThenBy(c => c.DisplayName).ToList(),
            IsInitiallyExpanded = true,
        }).OrderBy(g => g.GroupType).ToList();
    }

    private static CreditGroupType TvCreditGroup(string job) => job switch
    {
        "Actor" => CreditGroupType.Cast,
        "Director" => CreditGroupType.Directors,
        "Writer" or "Screenplay" or "Teleplay" or "Story" => CreditGroupType.Writers,
        "Producer" or "Executive Producer" or "Co-Producer" => CreditGroupType.Producers,
        _ => CreditGroupType.CreativeTeam,
    };
}
