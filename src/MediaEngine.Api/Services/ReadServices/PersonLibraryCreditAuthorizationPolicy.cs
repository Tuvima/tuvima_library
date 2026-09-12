using MediaEngine.Api.Services.Display;
using MediaEngine.Contracts.Persons;

namespace MediaEngine.Api.Services.ReadServices;

/// <summary>
/// Applies catalogue visibility to person credits without changing the
/// structural artwork identity selected by <see cref="PersonCreditReadService"/>.
/// </summary>
internal static class PersonLibraryCreditAuthorizationPolicy
{
    public static List<PersonLibraryCreditDto> Filter(
        IEnumerable<PersonLibraryCreditDto> credits,
        IReadOnlyList<DisplayWorkRow> visibleWorks)
    {
        var visibleByWork = visibleWorks
            .GroupBy(work => work.WorkId)
            .ToDictionary(group => group.Key, group => group.First());

        return credits
            .Where(credit => visibleByWork.ContainsKey(credit.WorkId))
            .Select(credit => Project(credit, visibleByWork[credit.WorkId]))
            .ToList();
    }

    private static PersonLibraryCreditDto Project(
        PersonLibraryCreditDto credit,
        DisplayWorkRow visibleWork)
    {
        var isTvShow = credit.CollectionId.HasValue
            && credit.MediaType?.Contains("tv", StringComparison.OrdinalIgnoreCase) == true;

        return new PersonLibraryCreditDto
        {
            WorkId = credit.WorkId,
            CollectionId = credit.CollectionId,
            MediaType = credit.MediaType,
            Title = credit.Title,
            // A person credit for TV represents the show, even though the owned
            // work authorizing it is an episode. Never replace the show's poster
            // with that episode's still.
            CoverUrl = isTvShow ? credit.CoverUrl : visibleWork.CoverUrl,
            Year = credit.Year,
            Role = credit.Role,
            AssociationType = credit.AssociationType,
            ViaGroupId = credit.ViaGroupId,
            ViaGroupName = credit.ViaGroupName,
            AssociationIsInferred = credit.AssociationIsInferred,
            Characters = credit.Characters,
        };
    }
}
