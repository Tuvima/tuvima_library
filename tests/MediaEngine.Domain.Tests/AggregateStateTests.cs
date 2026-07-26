using System.Reflection;
using MediaEngine.Domain.Aggregates;
using MediaEngine.Domain.Constants;
using MediaEngine.Domain.Enums;

namespace MediaEngine.Domain.Tests;

public sealed class AggregateStateTests
{
    [Fact]
    public void AggregateStateSerializers_RoundTripEveryEnumValue()
    {
        AssertRoundTrips<WikidataLinkStatus>(
            value => value.ToStorageValue(),
            AggregateStateSerializer.ParseWikidataLinkStatus);
        AssertRoundTrips<WorkMatchLevel>(
            value => value.ToStorageValue(),
            AggregateStateSerializer.ParseWorkMatchLevel);
        AssertRoundTrips<CollectionType>(
            value => value.ToStorageValue(),
            AggregateStateSerializer.ParseCollectionType);
        AssertRoundTrips<CollectionScope>(
            value => value.ToStorageValue(),
            AggregateStateSerializer.ParseCollectionScope);
        AssertRoundTrips<CollectionResolution>(
            value => value.ToStorageValue(),
            AggregateStateSerializer.ParseCollectionResolution);
        AssertRoundTrips<CollectionMatchMode>(
            value => value.ToStorageValue(),
            AggregateStateSerializer.ParseCollectionMatchMode);
        AssertRoundTrips<CollectionSortDirection>(
            value => value.ToStorageValue(),
            AggregateStateSerializer.ParseCollectionSortDirection);
        AssertRoundTrips<CollectionUniverseStatus>(
            value => value.ToStorageValue(),
            AggregateStateSerializer.ParseCollectionUniverseStatus);
    }

    [Fact]
    public void AggregateStateSerializers_RejectUnknownPersistedValues()
    {
        Assert.Throws<InvalidOperationException>(
            () => AggregateStateSerializer.ParseWikidataLinkStatus("future-state"));
        Assert.Throws<InvalidOperationException>(
            () => AggregateStateSerializer.ParseCollectionType("future-type"));
    }

    [Fact]
    public void Work_LinkToWikidata_TransitionsIdentityAsOneOperation()
    {
        var work = new Work();
        var checkedAt = DateTimeOffset.UtcNow;

        work.LinkToWikidata(
            "q190159",
            WorkMatchLevel.Edition,
            WikidataLinkStatus.UserConfirmed,
            checkedAt);

        Assert.Equal("Q190159", work.WikidataQid);
        Assert.Equal(WorkMatchLevel.Edition, work.MatchLevel);
        Assert.Equal(WikidataLinkStatus.UserConfirmed, work.WikidataStatus);
        Assert.Equal(checkedAt, work.WikidataCheckedAt);
        Assert.False(work.UniverseMismatch);
        Assert.Null(work.UniverseMismatchAt);
    }

    [Fact]
    public void Work_LinkToWikidata_RejectsUnlinkedStatesAndInvalidQids()
    {
        var work = new Work();

        Assert.Throws<ArgumentException>(
            () => work.LinkToWikidata("Dune", WorkMatchLevel.Work));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => work.LinkToWikidata(
                "Q190159",
                WorkMatchLevel.Work,
                WikidataLinkStatus.Pending));
    }

    [Fact]
    public void AggregateState_IsChangedThroughIntentionRevealingMethods()
    {
        AssertPrivateSetter<Work>(nameof(Work.WikidataStatus));
        AssertPrivateSetter<Work>(nameof(Work.MatchLevel));
        AssertPrivateSetter<Collection>(nameof(Collection.CollectionType));
        AssertPrivateSetter<Collection>(nameof(Collection.Scope));
        AssertPrivateSetter<Collection>(nameof(Collection.Resolution));
        AssertPrivateSetter<Collection>(nameof(Collection.MatchMode));
        AssertPrivateSetter<Collection>(nameof(Collection.SortDirection));
        AssertPrivateSetter<Collection>(nameof(Collection.UniverseStatus));
    }

    [Fact]
    public void Collection_UserVisibilityRequiresAnOwner()
    {
        var collection = new Collection();

        Assert.Throws<ArgumentException>(
            () => collection.SetVisibility(CollectionScope.User, profileId: null));

        var profileId = Guid.NewGuid();
        collection.SetVisibility(CollectionScope.User, profileId);

        Assert.Equal(CollectionScope.User, collection.Scope);
        Assert.Equal(profileId, collection.ProfileId);

        collection.SetVisibility(CollectionScope.Library, profileId);

        Assert.Equal(CollectionScope.Library, collection.Scope);
        Assert.Null(collection.ProfileId);
    }

    private static void AssertRoundTrips<TEnum>(
        Func<TEnum, string> serialize,
        Func<string, TEnum> parse)
        where TEnum : struct, Enum
    {
        foreach (var value in Enum.GetValues<TEnum>())
        {
            var persisted = serialize(value);
            Assert.False(string.IsNullOrWhiteSpace(persisted));
            Assert.Equal(value, parse(persisted));
        }
    }

    private static void AssertPrivateSetter<TAggregate>(string propertyName)
    {
        var property = typeof(TAggregate).GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public);

        Assert.NotNull(property);
        Assert.NotNull(property!.SetMethod);
        Assert.True(property.SetMethod!.IsPrivate);
    }
}
