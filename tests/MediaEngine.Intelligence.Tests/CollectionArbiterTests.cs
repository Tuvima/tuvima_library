using MediaEngine.Domain.Aggregates;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Models;
using MediaEngine.Domain.Services;
using MediaEngine.Intelligence.Models;
using MediaEngine.Intelligence.Strategies;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Intelligence.Tests;

/// <summary>
/// Tests for <see cref="CollectionArbiter"/> — evaluates a Work against Collection candidates
/// and decides whether to auto-link, flag for review, or reject.
/// </summary>
public sealed class CollectionArbiterTests
{
    private static readonly ScoringConfiguration DefaultConfig = new()
    {
        AutoLinkThreshold = 0.85,
        ConflictThreshold = 0.60,
    };

    // ── High similarity → AutoLinked ─────────────────────────────────────────

    [Fact]
    public async Task HighSimilarity_AutoLinked()
    {
        var journal = new StubJournal();
        var arbiter = new CollectionArbiter(new IdentityMatcher(new StubFuzzyMatchingService(), new ExactMatchStrategy()), journal);

        var work = MakeWork("Dune", "Frank Herbert");
        var collection  = MakeCollection(MakeWork("Dune", "Frank Herbert"));

        var decision = await arbiter.EvaluateAsync(
            work, [collection], new Dictionary<Guid, double>(), DefaultConfig);

        Assert.Equal(LinkDisposition.AutoLinked, decision.Disposition);
        Assert.Equal(collection.Id, decision.CollectionId);
        Assert.True(decision.Score >= DefaultConfig.AutoLinkThreshold);
    }

    // ── Low similarity → Rejected ────────────────────────────────────────────

    [Fact]
    public async Task LowSimilarity_Rejected()
    {
        var journal = new StubJournal();
        var arbiter = new CollectionArbiter(new IdentityMatcher(new StubFuzzyMatchingService(), new ExactMatchStrategy()), journal);

        var work = MakeWork("Dune", "Frank Herbert");
        var collection  = MakeCollection(MakeWork("War and Peace", "Leo Tolstoy"));

        var decision = await arbiter.EvaluateAsync(
            work, [collection], new Dictionary<Guid, double>(), DefaultConfig);

        Assert.Equal(LinkDisposition.Rejected, decision.Disposition);
        Assert.Equal(Guid.Empty, decision.CollectionId);
    }

    // ── No candidates → Rejected ─────────────────────────────────────────────

    [Fact]
    public async Task NoCandidates_Rejected()
    {
        var journal = new StubJournal();
        var arbiter = new CollectionArbiter(new IdentityMatcher(new StubFuzzyMatchingService(), new ExactMatchStrategy()), journal);

        var work = MakeWork("Dune", "Frank Herbert");

        var decision = await arbiter.EvaluateAsync(
            work, [], new Dictionary<Guid, double>(), DefaultConfig);

        Assert.Equal(LinkDisposition.Rejected, decision.Disposition);
        Assert.Equal(0.0, decision.Score);
    }

    // ── Circular link guard ──────────────────────────────────────────────────

    [Fact]
    public async Task CircularLink_Skipped()
    {
        var journal = new StubJournal();
        var arbiter = new CollectionArbiter(new IdentityMatcher(new StubFuzzyMatchingService(), new ExactMatchStrategy()), journal);

        var collection = new Collection { Id = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow };

        // Work already belongs to this collection.
        var work = MakeWork("Dune", "Frank Herbert");
        work.CollectionId = collection.Id;

        // Collection contains a different work with same title — but the work already
        // belongs to this collection, so the collection itself is skipped.
        collection.Works.Add(MakeWork("Dune", "Frank Herbert"));

        var decision = await arbiter.EvaluateAsync(
            work, [collection], new Dictionary<Guid, double>(), DefaultConfig);

        // Work already in this collection → collection skipped → effectively rejected (score 0).
        Assert.Equal(LinkDisposition.Rejected, decision.Disposition);
    }

    // ── Best collection selected among multiple candidates ──────────────────────────

    [Fact]
    public async Task BestCollection_SelectedAmongMultiple()
    {
        var journal = new StubJournal();
        var arbiter = new CollectionArbiter(new IdentityMatcher(new StubFuzzyMatchingService(), new ExactMatchStrategy()), journal);

        var work     = MakeWork("Dune", "Frank Herbert");
        var goodCollection  = MakeCollection(MakeWork("Dune", "Frank Herbert"));
        var badCollection   = MakeCollection(MakeWork("Foundation", "Isaac Asimov"));

        var decision = await arbiter.EvaluateAsync(
            work, [badCollection, goodCollection], new Dictionary<Guid, double>(), DefaultConfig);

        Assert.Equal(goodCollection.Id, decision.CollectionId);
        Assert.Equal(LinkDisposition.AutoLinked, decision.Disposition);
    }

    // ── Transaction journal receives event ───────────────────────────────────

    [Fact]
    public async Task Decision_LoggedToJournal()
    {
        var journal = new StubJournal();
        var arbiter = new CollectionArbiter(new IdentityMatcher(new StubFuzzyMatchingService(), new ExactMatchStrategy()), journal);

        var work = MakeWork("Dune", "Frank Herbert");
        var collection  = MakeCollection(MakeWork("Dune", "Frank Herbert"));

        await arbiter.EvaluateAsync(
            work, [collection], new Dictionary<Guid, double>(), DefaultConfig);

        Assert.Single(journal.Entries);
        Assert.Equal("WORK_AUTO_LINKED", journal.Entries[0].EventType);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static Work MakeWork(string title, string author)
    {
        var work = new Work { Id = Guid.NewGuid() };
        work.CanonicalValues.Add(new CanonicalValue
        {
            EntityId = work.Id, Key = "title", Value = title,
            LastScoredAt = DateTimeOffset.UtcNow,
        });
        work.CanonicalValues.Add(new CanonicalValue
        {
            EntityId = work.Id, Key = "author", Value = author,
            LastScoredAt = DateTimeOffset.UtcNow,
        });
        return work;
    }

    private static Collection MakeCollection(Work work)
    {
        var collection = new Collection { Id = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow };
        collection.Works.Add(work);
        return collection;
    }
}

// ── Stub transaction journal ─────────────────────────────────────────────────

file sealed class StubJournal : ITransactionJournal
{
    public List<(string EventType, string EntityType, string EntityId)> Entries { get; } = [];

    public void Log(string eventType, string entityType, string entityId) =>
        Entries.Add((eventType, entityType, entityId));

    public void Prune(int maxEntries = 100_000) { }
}
