using MediaEngine.Providers.Models;
using MediaEngine.Providers.Services;
using MediaEngine.Domain.Entities;

namespace MediaEngine.Providers.Tests;

public sealed class PersonGroupIdentityTests
{
    [Fact]
    public void NirvanaStyleClaims_AreGroupMembersRatherThanPseudonymAliases()
    {
        IReadOnlyList<ProviderClaim> claims =
        [
            new("instance_of_qid", "Q215380::musical group", 1),
            new("has_parts_qid", "Q8446::Kurt Cobain", 1),
            new("has_parts_qid", "Q111054::Krist Novoselic", 1),
            new("has_parts_qid", "Q12006::Dave Grohl", 1),
            new("has_parts_qid", "Q12006::Dave Grohl", 1),
        ];

        var isGroup = MetadataHarvestingService.IsMusicalGroup(claims);

        Assert.True(isGroup);
        Assert.False(MetadataHarvestingService.IsPseudonym(claims, isGroup));
        Assert.Equal(
            ["Q8446", "Q111054", "Q12006"],
            MetadataHarvestingService.GetGroupMemberQids(claims));

        var members = MetadataHarvestingService.GetGroupMemberReferences(claims);
        Assert.Equal("Kurt Cobain", members[0].Label);
        Assert.Equal("Krist Novoselic", members[1].Label);
        Assert.Equal("Dave Grohl", members[2].Label);
    }

    [Fact]
    public void CollectivePenNameClaims_RemainPseudonymRelationships()
    {
        IReadOnlyList<ProviderClaim> claims =
        [
            new("instance_of_qid", "Q16017119::collective pseudonym", 1),
            new("has_parts_qid", "Q123::First Author", 1),
            new("has_parts_qid", "Q456::Second Author", 1),
        ];

        var isGroup = MetadataHarvestingService.IsMusicalGroup(claims);

        Assert.False(isGroup);
        Assert.True(MetadataHarvestingService.IsPseudonym(claims, isGroup));
    }

    [Theory]
    [InlineData("Name pending (Q1990978)")]
    [InlineData("Unknown Person (Q1990978)")]
    [InlineData("Q1990978")]
    public void InternalFallbackNames_AreNeverAcceptedAsDisplayNames(string value)
    {
        Assert.True(MetadataHarvestingService.IsPlaceholderPersonName(value));
    }

    [Fact]
    public void GroupMemberReferences_PreserveBestLabelWhileDeduplicatingQid()
    {
        IReadOnlyList<ProviderClaim> claims =
        [
            new("has_parts_qid", "Q106193::James Hetfield", 1),
            new("has_parts_qid", "Q106193::Name pending (Q106193)", 1),
            new("has_parts_qid", "Q484302::Lars Ulrich", 1),
        ];

        var members = MetadataHarvestingService.GetGroupMemberReferences(claims);

        Assert.Collection(
            members,
            member =>
            {
                Assert.Equal("Q106193", member.Qid);
                Assert.Equal("James Hetfield", member.Label);
            },
            member =>
            {
                Assert.Equal("Q484302", member.Qid);
                Assert.Equal("Lars Ulrich", member.Label);
            });
    }

    [Fact]
    public void EnrichedGroup_RemainsRefreshableForMembershipChanges()
    {
        var group = new Person
        {
            Name = "Metallica",
            IsGroup = true,
            Biography = "American heavy metal band.",
            HeadshotUrl = "https://example.test/metallica.jpg",
            EnrichedAt = DateTimeOffset.UtcNow,
        };

        Assert.True(RecursiveIdentityService.NeedsProfileBackfill(group));
    }
}
