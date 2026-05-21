using MediaEngine.Domain.Aggregates;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;

namespace MediaEngine.Domain.Tests;

public sealed class CollectionAggregateTests
{
    [Fact]
    public void Works_ExposesNonCastableReadOnlyView()
    {
        var collection = new Collection();

        Assert.Empty(collection.Works);
        Assert.False(collection.Works is List<Work>);
    }

    [Fact]
    public void AddWork_AddsWorkAndIgnoresDuplicateId()
    {
        var workId = Guid.NewGuid();
        var collection = new Collection();
        var work = new Work { Id = workId, MediaType = MediaType.Books };

        collection.AddWork(work);
        collection.AddWork(new Work { Id = workId, MediaType = MediaType.Movies });

        Assert.Single(collection.Works);
        Assert.Same(work, collection.Works[0]);
    }

    [Fact]
    public void AddWork_RejectsNull()
    {
        var collection = new Collection();

        Assert.Throws<ArgumentNullException>(() => collection.AddWork(null!));
    }

    [Fact]
    public void AddWorks_AddsMultipleWorks()
    {
        var collection = new Collection();
        var works = new[]
        {
            new Work { Id = Guid.NewGuid(), MediaType = MediaType.Books },
            new Work { Id = Guid.NewGuid(), MediaType = MediaType.Movies },
        };

        collection.AddWorks(works);

        Assert.Equal(2, collection.Works.Count);
    }

    [Fact]
    public void AddRelationship_AddsRelationshipAndIgnoresDuplicateId()
    {
        var relationshipId = Guid.NewGuid();
        var collection = new Collection();
        var relationship = new CollectionRelationship
        {
            Id = relationshipId,
            CollectionId = collection.Id,
            RelType = "series",
            RelQid = "Q1",
        };

        collection.AddRelationship(relationship);
        collection.AddRelationship(new CollectionRelationship
        {
            Id = relationshipId,
            CollectionId = collection.Id,
            RelType = "series",
            RelQid = "Q1",
        });

        Assert.Single(collection.Relationships);
        Assert.Same(relationship, collection.Relationships[0]);
    }

    [Fact]
    public void AddChildCollection_AddsChildAndIgnoresDuplicateId()
    {
        var childId = Guid.NewGuid();
        var collection = new Collection();
        var child = new Collection { Id = childId, DisplayName = "Dune Novels" };

        collection.AddChildCollection(child);
        collection.AddChildCollection(new Collection { Id = childId, DisplayName = "Dune Films" });

        Assert.Single(collection.ChildCollections);
        Assert.Same(child, collection.ChildCollections[0]);
    }
}
