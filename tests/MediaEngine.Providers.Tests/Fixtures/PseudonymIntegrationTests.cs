using Microsoft.Extensions.DependencyInjection;
using Tuvima.Wikidata;
using Xunit.Abstractions;

namespace MediaEngine.Providers.Tests.Fixtures;

/// <summary>
/// Live-Wikidata integration tests for the three pseudonym patterns that
/// <c>ReconciliationAdapter.FetchWorkAsync</c> relies on. Phase 5 of the
/// adapter slimdown remediation introduced Pattern 1 and Pattern 2 detection
/// via <c>Tuvima.Wikidata.Authors.ResolveAsync</c>. These tests call the
/// library directly (rather than the full adapter) so the fixture is fast
/// and focused on the library contract the adapter depends on.
///
/// <para>
/// Pattern 1 — reverse P742 lookup: "Richard Bachman" → Stephen King.
/// Pattern 2 — P742 enumeration: "Stephen King" → ["Richard Bachman", ...].
/// Pattern 3 — collective pseudonym: "James S.A. Corey" → Daniel Abraham + Ty Franck.
/// </para>
///
/// <para>
/// All three tests are marked <c>[Skip]</c> by default because they hit
/// live Wikidata and are slow. Run locally with:
/// </para>
/// <code>
/// dotnet test --filter "FullyQualifiedName~PseudonymIntegrationTests"
/// </code>
/// </summary>
[Trait("Category", "Integration")]
public sealed class PseudonymIntegrationTests : IDisposable
{
    // Known QIDs for assertion targets. Verified against live Wikidata
    // at the time the tests were written — confirmed via the output of
    // PseudonymIntegrationTests on 2026-04-08.
    private const string StephenKingQid   = "Q39829";
    private const string DanielAbrahamQid = "Q1159871";
    private const string TyFranckQid      = "Q18608460";

    private readonly ITestOutputHelper _output;
    private readonly WikidataReconciler _reconciler;
    private readonly ServiceProvider _sp;

    public PseudonymIntegrationTests(ITestOutputHelper output)
    {
        _output = output;

        var services = new ServiceCollection();
        services.AddHttpClient("WikidataReconciliation", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(60);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Tuvima Library/PseudonymIntegration (mailto:test@tuvima.dev)");
        });
        _sp = services.BuildServiceProvider();

        var factory = _sp.GetRequiredService<IHttpClientFactory>();
        _reconciler = new WikidataReconciler(
            factory.CreateClient("WikidataReconciliation"),
            new WikidataReconcilerOptions
            {
                UserAgent             = "Tuvima Library/PseudonymIntegration (mailto:test@tuvima.dev)",
                MaxLag                = 0,
                TypeHierarchyDepth    = 3,
                IncludeSitelinkLabels = true,
            });
    }

    [Fact(Skip = "Live Wikidata integration — run manually with --filter \"FullyQualifiedName~PseudonymIntegrationTests\"")]
    [Trait("Category", "Integration")]
    public async Task Pattern1_RichardBachman_ResolvesToStephenKing()
    {
        // "Richard Bachman" should resolve to Stephen King's QID. The library
        // has two paths that can produce this result:
        //   (a) The initial reconciliation matches a label or alias on Stephen
        //       King's entity and scores ≥ 80 — Pattern 1 is skipped because
        //       the match is already high-confidence.
        //   (b) The initial reconciliation scores below 80, triggering the
        //       Pattern 1 reverse-P742 CirrusSearch; when a hit is found,
        //       both Qid and RealNameQid are set to the real author's QID.
        // Either path is correct and this test accepts both.
        var result = await _reconciler.Authors.ResolveAsync(new AuthorResolutionRequest
        {
            RawAuthorString  = "Richard Bachman",
            DetectPseudonyms = true,
        });

        Assert.NotNull(result);
        Assert.NotEmpty(result.Authors);

        var resolved = result.Authors.First();
        _output.WriteLine(
            $"Pattern 1: '{resolved.OriginalName}' → Qid={resolved.Qid ?? "<none>"}, " +
            $"RealNameQid={resolved.RealNameQid ?? "<none>"}, " +
            $"Canonical='{resolved.CanonicalName ?? "<none>"}', " +
            $"Score={resolved.Confidence:F2}");

        // The critical invariant: whichever path fired, Qid must resolve to
        // Stephen King. The adapter's BridgeIdKeys.AuthorRealNameQid claim is
        // only emitted when RealNameQid is populated (path b), but the main
        // author_qid claim is emitted either way.
        Assert.Equal(StephenKingQid, resolved.Qid);
        Assert.Equal("Stephen King", resolved.CanonicalName);
    }

    [Fact(Skip = "Live Wikidata integration — run manually with --filter \"FullyQualifiedName~PseudonymIntegrationTests\"")]
    [Trait("Category", "Integration")]
    public async Task Pattern2_StephenKing_EnumeratesPseudonyms()
    {
        // When a raw author string is the REAL author and Wikidata has P742
        // claims on that entity, Pseudonyms should be populated with the raw
        // string values. Stephen King has (at least) Richard Bachman.
        var result = await _reconciler.Authors.ResolveAsync(new AuthorResolutionRequest
        {
            RawAuthorString  = "Stephen King",
            DetectPseudonyms = true,
        });

        Assert.NotNull(result);
        Assert.NotEmpty(result.Authors);

        var resolved = result.Authors.First();
        _output.WriteLine(
            $"Pattern 2: '{resolved.OriginalName}' → Qid={resolved.Qid}, " +
            $"Pseudonyms=[{string.Join(", ", resolved.Pseudonyms ?? [])}]");

        Assert.Equal(StephenKingQid, resolved.Qid);
        Assert.NotNull(resolved.Pseudonyms);
        Assert.Contains(
            resolved.Pseudonyms!,
            p => p.Contains("Bachman", StringComparison.OrdinalIgnoreCase));

        // Pattern 2 should NOT fire Pattern 1 on the same call — RealNameQid
        // is reserved for pen-name-input resolution.
        Assert.Null(resolved.RealNameQid);
    }

    [Fact(Skip = "Live Wikidata integration — run manually with --filter \"FullyQualifiedName~PseudonymIntegrationTests\"")]
    [Trait("Category", "Integration")]
    public async Task Pattern3_JamesSACorey_ExpandsToRealAuthors()
    {
        // Collective pseudonym: "James S.A. Corey" resolves to its own Wikidata
        // entity (Q6142591) which has P31 = Q16017119 (collective pseudonym)
        // and P527 listing the two real authors. RealAuthors should contain
        // both Daniel Abraham and Ty Franck.
        var result = await _reconciler.Authors.ResolveAsync(new AuthorResolutionRequest
        {
            RawAuthorString  = "James S.A. Corey",
            DetectPseudonyms = true,
        });

        Assert.NotNull(result);
        Assert.NotEmpty(result.Authors);

        var resolved = result.Authors.First();
        _output.WriteLine(
            $"Pattern 3: '{resolved.OriginalName}' → Qid={resolved.Qid}, " +
            $"Canonical='{resolved.CanonicalName}', " +
            $"RealAuthors count={resolved.RealAuthors?.Count ?? 0}");

        if (resolved.RealAuthors is not null)
        {
            foreach (var real in resolved.RealAuthors)
                _output.WriteLine($"    — {real.CanonicalName} ({real.Qid})");
        }

        Assert.NotNull(resolved.Qid);
        Assert.NotNull(resolved.RealAuthors);
        Assert.True(
            resolved.RealAuthors!.Count >= 2,
            $"Expected at least 2 real authors for collective pseudonym, got {resolved.RealAuthors.Count}");

        var realAuthorQids = resolved.RealAuthors
            .Select(r => r.Qid)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains(DanielAbrahamQid, realAuthorQids);
        Assert.Contains(TyFranckQid,       realAuthorQids);

        // Pattern 3 is mutually exclusive with Pattern 1 on the same result.
        Assert.Null(resolved.RealNameQid);
    }

    [Fact(Skip = "Live Wikidata integration — run manually with --filter \"FullyQualifiedName~PseudonymIntegrationTests\"")]
    [Trait("Category", "Integration")]
    public async Task MultiAuthor_GoodOmens_ResolvesBothAuthorsIndependently()
    {
        // Parity case from the slimdown acceptance criteria: "Good Omens" is
        // credited to "Neil Gaiman & Terry Pratchett". The library should
        // split on " & " and return two independent ResolvedAuthor entries,
        // each with a Qid and neither with Pattern 1/3 fields set.
        var result = await _reconciler.Authors.ResolveAsync(new AuthorResolutionRequest
        {
            RawAuthorString  = "Neil Gaiman & Terry Pratchett",
            DetectPseudonyms = true,
        });

        Assert.NotNull(result);
        Assert.Equal(2, result.Authors.Count);

        foreach (var author in result.Authors)
        {
            _output.WriteLine(
                $"Multi-author: '{author.OriginalName}' → Qid={author.Qid}, " +
                $"Canonical='{author.CanonicalName}'");

            Assert.NotNull(author.Qid);
            Assert.NotNull(author.CanonicalName);
            Assert.Null(author.RealNameQid);
            Assert.Null(author.RealAuthors);
        }

        // Verify the two names resolved to distinct entities.
        var qids = result.Authors.Select(a => a.Qid).ToHashSet();
        Assert.Equal(2, qids.Count);
    }

    public void Dispose()
    {
        _reconciler.Dispose();
        _sp.Dispose();
    }
}
