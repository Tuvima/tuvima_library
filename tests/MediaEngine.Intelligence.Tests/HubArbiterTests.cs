using MediaEngine.Domain.Aggregates;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Models;
using MediaEngine.Domain.Services;
using MediaEngine.Intelligence.Models;
using MediaEngine.Intelligence.Strategies;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Intelligence.Tests;

/// <summary>
/// Tests for <see cref="HubArbiter"/> — evaluates a Work against Hub candidates
/// and decides whether to auto-link, flag for review, or reject.
/// </summary>
public sealed class HubArbiterTests
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
        var arbiter = new HubArbiter(new IdentityMatcher(new StubFuzzyMatchingService(), new ExactMatchStrategy()), journal);

        var work = MakeWork("Dune", "Frank Herbert");
        var hub  = MakeHub(MakeWork("Dune", "Frank Herbert"));

        var decision = await arbiter.EvaluateAsync(
            work, [hub], new Dictionary<Guid, double>(), DefaultConfig);

        Assert.Equal(LinkDisposition.AutoLinked, decision.Disposition);
        Assert.Equal(hub.Id, decision.HubId);
        Assert.True(decision.Score >= DefaultConfig.AutoLinkThreshold);
    }

    // ── Low similarity → Rejected ────────────────────────────────────────────

    [Fact]
    public async Task LowSimilarity_Rejected()
    {
        var journal = new StubJournal();
        var arbiter = new HubArbiter(new IdentityMatcher(new StubFuzzyMatchingService(), new ExactMatchStrategy()), journal);

        var work = MakeWork("Dune", "Frank Herbert");
        var hub  = MakeHub(MakeWork("War and Peace", "Leo Tolstoy"));

        var decision = await arbiter.EvaluateAsync(
            work, [hub], new Dictionary<Guid, double>(), DefaultConfig);

        Assert.Equal(LinkDisposition.Rejected, decision.Disposition);
        Assert.Equal(Guid.Empty, decision.HubId);
    }

    // ── No candidates → Rejected ─────────────────────────────────────────────

    [Fact]
    public async Task NoCandidates_Rejected()
    {
        var journal = new StubJournal();
        var arbiter = new HubArbiter(new IdentityMatcher(new StubFuzzyMatchingService(), new ExactMatchStrategy()), journal);

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
        var arbiter = new HubArbiter(new IdentityMatcher(new StubFuzzyMatchingService(), new ExactMatchStrategy()), journal);

        var hub = new Hub { Id = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow };

        // Work already belongs to this hub.
        var work = MakeWork("Dune", "Frank Herbert");
        work.HubId = hub.Id;

        // Hub contains a different work with same title — but the work already
        // belongs to this hub, so the hub itself is skipped.
        hub.Works.Add(MakeWork("Dune", "Frank Herbert"));

        var decision = await arbiter.EvaluateAsync(
            work, [hub], new Dictionary<Guid, double>(), DefaultConfig);

        // Work already in this hub → hub skipped → effectively rejected (score 0).
        Assert.Equal(LinkDisposition.Rejected, decision.Disposition);
    }

    // ── Best hub selected among multiple candidates ──────────────────────────

    [Fact]
    public async Task BestHub_SelectedAmongMultiple()
    {
        var journal = new StubJournal();
        var arbiter = new HubArbiter(new IdentityMatcher(new StubFuzzyMatchingService(), new ExactMatchStrategy()), journal);

        var work     = MakeWork("Dune", "Frank Herbert");
        var goodHub  = MakeHub(MakeWork("Dune", "Frank Herbert"));
        var badHub   = MakeHub(MakeWork("Foundation", "Isaac Asimov"));

        var decision = await arbiter.EvaluateAsync(
            work, [badHub, goodHub], new Dictionary<Guid, double>(), DefaultConfig);

        Assert.Equal(goodHub.Id, decision.HubId);
        Assert.Equal(LinkDisposition.AutoLinked, decision.Disposition);
    }

    // ── Transaction journal receives event ───────────────────────────────────

    [Fact]
    public async Task Decision_LoggedToJournal()
    {
        var journal = new StubJournal();
        var arbiter = new HubArbiter(new IdentityMatcher(new StubFuzzyMatchingService(), new ExactMatchStrategy()), journal);

        var work = MakeWork("Dune", "Frank Herbert");
        var hub  = MakeHub(MakeWork("Dune", "Frank Herbert"));

        await arbiter.EvaluateAsync(
            work, [hub], new Dictionary<Guid, double>(), DefaultConfig);

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

    private static Hub MakeHub(Work work)
    {
        var hub = new Hub { Id = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow };
        hub.Works.Add(work);
        return hub;
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
