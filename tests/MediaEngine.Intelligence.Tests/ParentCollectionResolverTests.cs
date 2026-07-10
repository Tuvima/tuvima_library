using MediaEngine.Domain.Aggregates;
using MediaEngine.Domain.Entities;
using MediaEngine.Intelligence;
using MediaEngine.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaEngine.Intelligence.Tests;

public sealed class ParentCollectionResolverTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DatabaseConnection _db;
    private readonly CollectionRepository _repo;
    private readonly ParentCollectionResolver _resolver;

    public ParentCollectionResolverTests()
    {
        DapperConfiguration.Configure();
        _dbPath = Path.Combine(Path.GetTempPath(), $"tuvima_parent_collection_{Guid.NewGuid():N}.db");
        _db = new DatabaseConnection(_dbPath);
        _db.InitializeSchema();
        _db.RunStartupChecks();
        _repo = new CollectionRepository(_db);
        _resolver = new ParentCollectionResolver(_repo, NullLogger<ParentCollectionResolver>.Instance);
    }

    public void Dispose()
    {
        try { _db.Dispose(); } catch { }
        try { File.Delete(_dbPath); } catch { }
    }

    [Fact]
    public async Task ResolveParentCollectionAsync_CreatesParentForSharedBroaderFranchiseQid()
    {
        var books = CreateCollection("Dune book series", "ContentGroup", "Q6095696");
        var movies = CreateCollection("Dune film series", "ContentGroup", "Q109300883");
        await _repo.UpsertAsync(books);
        await _repo.UpsertAsync(movies);
        await _repo.InsertRelationshipsAsync(
        [
            CreateRelationship(books.Id, "franchise", "Q18011049", "Dune"),
            CreateRelationship(movies.Id, "franchise", "Q18011049", "Dune"),
        ]);

        await _resolver.ResolveParentCollectionAsync(books.Id);

        var refreshedBooks = await _repo.GetByIdAsync(books.Id);
        var refreshedMovies = await _repo.GetByIdAsync(movies.Id);
        Assert.NotNull(refreshedBooks);
        Assert.NotNull(refreshedBooks!.ParentCollectionId);
        Assert.NotNull(refreshedMovies);
        Assert.Equal(refreshedBooks.ParentCollectionId, refreshedMovies!.ParentCollectionId);

        var parent = await _repo.GetByIdAsync(refreshedBooks.ParentCollectionId!.Value);
        Assert.NotNull(parent);
        Assert.Equal("Universe", parent!.CollectionType);
        Assert.Equal("Dune", parent.DisplayName);
        Assert.Equal("Q18011049", parent.WikidataQid);

        var children = await _repo.GetChildCollectionsAsync(parent.Id);
        Assert.Contains(children, candidate => candidate.Id == books.Id);
        Assert.Contains(children, candidate => candidate.Id == movies.Id);
    }

    [Fact]
    public async Task ResolveParentCollectionAsync_DoesNotCreateParentForSingleBroaderFranchiseShelf()
    {
        var child = CreateCollection("The Matrix film series", "ContentGroup", "Q1228705");
        await _repo.UpsertAsync(child);
        await _repo.InsertRelationshipsAsync([CreateRelationship(child.Id, "franchise", "Q83495", "The Matrix")]);

        await _resolver.ResolveParentCollectionAsync(child.Id);

        var refreshedChild = await _repo.GetByIdAsync(child.Id);
        Assert.NotNull(refreshedChild);
        Assert.Null(refreshedChild!.ParentCollectionId);

        var parent = await _repo.FindParentCollectionByRelationshipAsync("Q83495");
        Assert.Null(parent);
    }

    [Fact]
    public async Task ResolveParentCollectionAsync_CreatesParentForSharedBroaderSeriesQid()
    {
        var lotr = CreateCollection("The Lord of the Rings", "ContentGroup", "Q190214");
        var hobbit = CreateCollection("The Hobbit trilogy", "ContentGroup", "Q74331");
        await _repo.UpsertAsync(lotr);
        await _repo.UpsertAsync(hobbit);
        await _repo.InsertRelationshipsAsync(
        [
            CreateRelationship(lotr.Id, "series", "Q26214973", "Peter Jackson's Middle-earth film series"),
            CreateRelationship(hobbit.Id, "series", "Q26214973", "Peter Jackson's Middle-earth film series"),
        ]);

        await _resolver.ResolveParentCollectionAsync(hobbit.Id);

        var refreshedLotr = await _repo.GetByIdAsync(lotr.Id);
        var refreshedHobbit = await _repo.GetByIdAsync(hobbit.Id);
        Assert.NotNull(refreshedLotr);
        Assert.NotNull(refreshedHobbit);
        Assert.NotNull(refreshedHobbit!.ParentCollectionId);
        Assert.Equal(refreshedHobbit.ParentCollectionId, refreshedLotr!.ParentCollectionId);

        var parent = await _repo.GetByIdAsync(refreshedHobbit.ParentCollectionId!.Value);
        Assert.NotNull(parent);
        Assert.Equal("Peter Jackson's Middle-earth film series", parent!.DisplayName);
        Assert.Equal("Q26214973", parent.WikidataQid);
    }

    [Fact]
    public async Task ResolveParentCollectionAsync_CreatesParentForSharedBasedOnQid()
    {
        var comics = CreateCollection("Batman", "ContentGroup", "Q2633138");
        var movies = CreateCollection("Batman in film", "ContentGroup", "Q2111133");
        await _repo.UpsertAsync(comics);
        await _repo.UpsertAsync(movies);
        await _repo.InsertRelationshipsAsync(
        [
            CreateRelationship(comics.Id, "based_on", "Q2695156", "Batman"),
            CreateRelationship(movies.Id, "based_on", "Q2695156", "Batman"),
        ]);

        await _resolver.ResolveParentCollectionAsync(comics.Id);

        var refreshedComics = await _repo.GetByIdAsync(comics.Id);
        var refreshedMovies = await _repo.GetByIdAsync(movies.Id);
        Assert.NotNull(refreshedComics);
        Assert.NotNull(refreshedMovies);
        Assert.NotNull(refreshedComics!.ParentCollectionId);
        Assert.Equal(refreshedComics.ParentCollectionId, refreshedMovies!.ParentCollectionId);

        var parent = await _repo.GetByIdAsync(refreshedComics.ParentCollectionId!.Value);
        Assert.NotNull(parent);
        Assert.Equal("Batman", parent!.DisplayName);
        Assert.Equal("Q2695156", parent.WikidataQid);
    }

    [Fact]
    public async Task ResolveParentCollectionAsync_RollsBookSeriesAndTvAdaptationIntoFranchiseAcrossRelationshipTypes()
    {
        var books = CreateCollection("The Expanse books", "ContentGroup", "Q19610143");
        var television = CreateCollection("The Expanse TV series", "ContentGroup", "Q18389644");
        await _repo.UpsertAsync(books);
        await _repo.UpsertAsync(television);
        await _repo.InsertRelationshipsAsync(
        [
            CreateRelationship(books.Id, "series", "Q19610143", "The Expanse"),
            CreateRelationship(television.Id, "based_on", "Q19610143", "The Expanse"),
        ]);

        await _resolver.ResolveParentCollectionAsync(television.Id);

        var refreshedBooks = await _repo.GetByIdAsync(books.Id);
        var refreshedTelevision = await _repo.GetByIdAsync(television.Id);
        Assert.NotNull(refreshedBooks?.ParentCollectionId);
        Assert.Equal(refreshedBooks!.ParentCollectionId, refreshedTelevision!.ParentCollectionId);

        var parent = await _repo.GetByIdAsync(refreshedBooks.ParentCollectionId!.Value);
        Assert.Equal("The Expanse", parent!.DisplayName);
        Assert.Equal("Q19610143", parent.WikidataQid);
    }

    [Fact]
    public async Task ResolveParentCollectionAsync_DoesNotCreateParentWhenRelationshipMatchesCollectionQid()
    {
        var child = CreateCollection("The Expanse", "ContentGroup", "Q19610143");
        await _repo.UpsertAsync(child);
        await _repo.InsertRelationshipsAsync([CreateRelationship(child.Id, "franchise", "Q19610143", "The Expanse")]);

        await _resolver.ResolveParentCollectionAsync(child.Id);

        var refreshedChild = await _repo.GetByIdAsync(child.Id);
        Assert.NotNull(refreshedChild);
        Assert.Null(refreshedChild!.ParentCollectionId);

        var parent = await _repo.FindParentCollectionByRelationshipAsync("Q19610143");
        Assert.Null(parent);
    }

    private static Collection CreateCollection(string name, string type, string qid) => new()
    {
        Id = Guid.NewGuid(),
        DisplayName = name,
        CollectionType = type,
        WikidataQid = qid,
        Resolution = "materialized",
        UniverseStatus = "Unknown",
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private static CollectionRelationship CreateRelationship(Guid collectionId, string relType, string qid, string label) => new()
    {
        Id = Guid.NewGuid(),
        CollectionId = collectionId,
        RelType = relType,
        RelQid = qid,
        RelLabel = label,
        Confidence = 1.0,
        DiscoveredAt = DateTimeOffset.UtcNow,
    };
}
