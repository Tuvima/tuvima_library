using MediaEngine.Providers.Services;
using Tuvima.Wikidata;

namespace MediaEngine.Providers.Tests;

public sealed class PersonReconciliationEvidenceTests
{
    [Fact]
    public void PerformerRejectsExactNameFootballerWithoutWorkEvidence()
    {
        var candidate = Candidate("Q4761465", ["association football player"]);

        Assert.False(PersonReconciliationService.HasCorroboratingIdentityEvidence(
            candidate, "Performer", "Part 01"));
    }

    [Fact]
    public void AuthorAcceptsCompatibleWriterOccupation()
    {
        var candidate = Candidate("Q18590295", ["writer", "novelist"]);

        Assert.True(PersonReconciliationService.HasCorroboratingIdentityEvidence(
            candidate, "Author", "Project Hail Mary"));
    }

    [Fact]
    public void PerformerAcceptsMusicalGroupWithoutHumanOccupation()
    {
        var candidate = Candidate("Q189644", [], isGroup: true);

        Assert.True(PersonReconciliationService.HasCorroboratingIdentityEvidence(
            candidate, "Performer", "Get Lucky"));
    }

    [Fact]
    public void UnknownRoleRequiresSpecificWorkEvidence()
    {
        var unsupported = Candidate("Q1", []);
        var supported = Candidate("Q2", [], notableWorks: ["Project Hail Mary"]);

        Assert.False(PersonReconciliationService.HasCorroboratingIdentityEvidence(
            unsupported, "Unknown", "Part 01"));
        Assert.True(PersonReconciliationService.HasCorroboratingIdentityEvidence(
            supported, "Unknown", "Project Hail Mary"));
    }

    private static PersonSearchResult Candidate(
        string qid,
        IReadOnlyList<string> occupations,
        bool isGroup = false,
        IReadOnlyList<string>? notableWorks = null) => new()
    {
        Found = true,
        Qid = qid,
        CanonicalName = "Same Name",
        Score = 1,
        IsGroup = isGroup,
        Occupations = occupations,
        NotableWorks = notableWorks ?? [],
    };
}
