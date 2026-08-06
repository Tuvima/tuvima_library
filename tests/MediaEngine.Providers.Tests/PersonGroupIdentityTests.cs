using MediaEngine.Providers.Models;
using MediaEngine.Providers.Services;

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
}
