using MediaEngine.Domain.Configuration;
using MediaEngine.Providers.Adapters;
using Tuvima.Wikidata;

namespace MediaEngine.Providers.Tests;

public sealed class FictionalEntityGraphEvidenceTests
{
    [Fact]
    public void BuildGraphEvidence_ExcludesDeprecatedClaimsAndRetainsTypedStatementContext()
    {
        var entity = new WikidataEntityInfo
        {
            Id = "Q-entity",
            Claims = new Dictionary<string, IReadOnlyList<WikidataClaim>>
            {
                ["P1080"] = [EntityClaim("P1080", "Q-universe", "Authoritative universe")],
                ["P1441"] =
                [
                    EntityClaim("P1441", "Q-deprecated-work", "Deprecated work", rank: "deprecated"),
                    new WikidataClaim
                    {
                        PropertyId = "P1441",
                        Rank = "normal",
                        Value = EntityValue("Q-present-work", "Present work"),
                        Qualifiers = new Dictionary<string, IReadOnlyList<WikidataValue>>
                        {
                            ["P10663"] = [EntityValue("Q-work-scope")],
                            ["P580"] = [TimeValue("+0001-01-01T00:00:00Z")],
                            ["P582"] = [TimeValue("+0002-01-01T00:00:00Z")],
                            ["P4895"] = [StringValue("chapter-7")],
                            ["P7528"] = [EntityValue("Q-spoiler-work")],
                        },
                        QualifierOrder = ["P10663", "P580", "P582", "P4895", "P7528"],
                    },
                ],
                ["P4584"] = [EntityClaim("P4584", "Q-first-work", "First work")],
            },
        };
        var propertyGroup = new DataExtensionPropertyGroup
        {
            Core = ["P1080", "P1441", "P4584"],
        };
        var labels = new Dictionary<string, string>
        {
            ["P1080"] = "narrative_universe",
            ["P1441"] = "present_in_work",
            ["P4584"] = "first_appearance",
        };

        var evidence = ReconciliationAdapter.BuildFictionalEntityGraphEvidence(entity, propertyGroup, labels);

        Assert.Equal("Q-universe", evidence.NarrativeUniverseQid);
        Assert.Equal("Authoritative universe", evidence.NarrativeUniverseLabel);
        Assert.DoesNotContain(evidence.Statements, statement => statement.TargetQid == "Q-deprecated-work");
        Assert.Contains(evidence.Statements, statement => statement.ClaimKey == "present_in_work_qid"
            && statement.TargetQid == "Q-present-work");
        Assert.Contains(evidence.Statements, statement => statement.ClaimKey == "first_appearance_qid"
            && statement.TargetQid == "Q-first-work");

        var presentInWork = Assert.Single(evidence.Statements, statement => statement.TargetQid == "Q-present-work");
        Assert.Contains(presentInWork.Qualifiers, qualifier => qualifier.PropertyId == "P10663"
            && qualifier.Value == "Q-work-scope" && qualifier.ValueKind == "WikidataQid");
        Assert.Contains(presentInWork.Qualifiers, qualifier => qualifier.PropertyId == "P580"
            && qualifier.ValueKind == "Time");
        Assert.Contains(presentInWork.Qualifiers, qualifier => qualifier.PropertyId == "P582"
            && qualifier.ValueKind == "Time");
        Assert.Contains(presentInWork.Qualifiers, qualifier => qualifier.PropertyId == "P4895"
            && qualifier.Value == "chapter-7" && qualifier.ValueKind == "Text");
        Assert.Contains(presentInWork.Qualifiers, qualifier => qualifier.PropertyId == "P7528"
            && qualifier.Value == "Q-spoiler-work" && qualifier.ValueKind == "WikidataQid");
    }

    private static WikidataClaim EntityClaim(string propertyId, string entityId, string? label = null, string rank = "normal") => new()
    {
        PropertyId = propertyId,
        Rank = rank,
        Value = EntityValue(entityId, label),
    };

    private static WikidataValue EntityValue(string entityId, string? label = null) => new()
    {
        Kind = WikidataValueKind.EntityId,
        EntityId = entityId,
        EntityLabel = label,
        RawValue = label ?? entityId,
    };

    private static WikidataValue TimeValue(string value) => new()
    {
        Kind = WikidataValueKind.Time,
        RawValue = value,
    };

    private static WikidataValue StringValue(string value) => new()
    {
        Kind = WikidataValueKind.String,
        RawValue = value,
    };
}
