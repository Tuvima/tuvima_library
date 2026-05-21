using MediaEngine.Domain.Aggregates;
using MediaEngine.Domain.Entities;

namespace MediaEngine.Domain.Tests;

public sealed class WorkAggregateTests
{
    [Fact]
    public void AggregateCollections_ExposeNonCastableReadOnlyViews()
    {
        var work = new Work();

        Assert.False(work.ExternalIdentifiers is Dictionary<string, string>);
        Assert.False(work.Editions is List<Edition>);
        Assert.False(work.MetadataClaims is List<MetadataClaim>);
        Assert.False(work.CanonicalValues is List<CanonicalValue>);
    }

    [Fact]
    public void SetExternalIdentifier_PreservesCaseInsensitiveKeyBehavior()
    {
        var work = new Work();

        work.SetExternalIdentifier("ISBN_13", "9780000000001");
        work.SetExternalIdentifier("isbn_13", "9780000000002");

        Assert.Single(work.ExternalIdentifiers);
        Assert.Equal("9780000000002", work.ExternalIdentifiers["ISBN_13"]);
        Assert.Equal("9780000000002", work.ExternalIdentifiers["isbn_13"]);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void SetExternalIdentifier_RejectsEmptyKey(string key)
    {
        var work = new Work();

        Assert.Throws<ArgumentException>(() => work.SetExternalIdentifier(key, "value"));
    }

    [Fact]
    public void SetExternalIdentifier_RejectsNullValue()
    {
        var work = new Work();

        Assert.Throws<ArgumentNullException>(() => work.SetExternalIdentifier("isbn_13", null!));
    }

    [Fact]
    public void AddEdition_AddsEditionAndIgnoresDuplicateId()
    {
        var editionId = Guid.NewGuid();
        var work = new Work();
        var edition = new Edition { Id = editionId, WorkId = work.Id };

        work.AddEdition(edition);
        work.AddEdition(new Edition { Id = editionId, WorkId = work.Id });

        Assert.Single(work.Editions);
        Assert.Same(edition, work.Editions[0]);
    }

    [Fact]
    public void AddMetadataClaim_PreservesAppendOnlyBehavior()
    {
        var work = new Work();
        var first = new MetadataClaim { Id = Guid.NewGuid(), EntityId = work.Id, ClaimKey = "title", ClaimValue = "First" };
        var second = new MetadataClaim { Id = Guid.NewGuid(), EntityId = work.Id, ClaimKey = "title", ClaimValue = "Second" };

        work.AddMetadataClaim(first);
        work.AddMetadataClaim(second);

        Assert.Equal(2, work.MetadataClaims.Count);
        Assert.Same(first, work.MetadataClaims[0]);
        Assert.Same(second, work.MetadataClaims[1]);
    }

    [Fact]
    public void AddCanonicalValue_PreservesCurrentDuplicateKeyBehavior()
    {
        var work = new Work();

        work.AddCanonicalValue(new CanonicalValue { EntityId = work.Id, Key = "title", Value = "First" });
        work.AddCanonicalValue(new CanonicalValue { EntityId = work.Id, Key = "title", Value = "Second" });

        Assert.Equal(2, work.CanonicalValues.Count);
    }
}
