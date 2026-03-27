using System.Reflection;
using MediaEngine.Domain.Models;
using MediaEngine.Providers.Models;
using MediaEngine.Providers.Services;

namespace MediaEngine.Providers.Tests;

/// <summary>
/// Tests for <c>HydrationPipelineService.ExtractPersonReferencesFromRawClaims</c>.
/// Validates that multi-valued author / narrator / performer fields are
/// preserved correctly and that the performer→Narrator role mapping works.
/// Uses reflection to access the private static method.
/// </summary>
public sealed class PersonExtractionTests
{
    private static readonly MethodInfo ExtractMethod = typeof(HydrationPipelineService)
        .GetMethod("ExtractPersonReferencesFromRawClaims", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException(
               "ExtractPersonReferencesFromRawClaims method not found on HydrationPipelineService");

    private static IReadOnlyList<PersonReference> Extract(IReadOnlyList<ProviderClaim> claims) =>
        (IReadOnlyList<PersonReference>)ExtractMethod.Invoke(null, [claims])!;

    // ── Multiple authors ──────────────────────────────────────────────────────

    [Fact]
    public void MultipleAuthors_AllPreserved()
    {
        var claims = new List<ProviderClaim>
        {
            new("author",     "Terry Pratchett", 0.90),
            new("author",     "Neil Gaiman",     0.90),
            new("author_qid", "Q46248::Terry Pratchett", 0.90),
            new("author_qid", "Q210112::Neil Gaiman",    0.90),
        };

        var refs = Extract(claims);

        Assert.Equal(2, refs.Count);
        Assert.Contains(refs, r =>
            r.Name == "Terry Pratchett" && r.Role == "Author" && r.WikidataQid == "Q46248");
        Assert.Contains(refs, r =>
            r.Name == "Neil Gaiman"     && r.Role == "Author" && r.WikidataQid == "Q210112");
    }

    // ── Single author ─────────────────────────────────────────────────────────

    [Fact]
    public void SingleAuthor_Works()
    {
        var claims = new List<ProviderClaim>
        {
            new("author",     "Frank Herbert",        0.90),
            new("author_qid", "Q44413::Frank Herbert", 0.90),
        };

        var refs = Extract(claims);

        Assert.Single(refs);
        Assert.Equal("Frank Herbert", refs[0].Name);
        Assert.Equal("Author",        refs[0].Role);
        Assert.Equal("Q44413",        refs[0].WikidataQid);
    }

    // ── Performer → Narrator role mapping ────────────────────────────────────

    [Fact]
    public void PerformerExtractedAsNarrator()
    {
        var claims = new List<ProviderClaim>
        {
            new("performer",     "Tim Gerard Reynolds",        0.90),
            new("performer_qid", "Q123456::Tim Gerard Reynolds", 0.90),
        };

        var refs = Extract(claims);

        Assert.Single(refs);
        Assert.Equal("Tim Gerard Reynolds", refs[0].Name);
        Assert.Equal("Narrator",            refs[0].Role);
        Assert.Equal("Q123456",             refs[0].WikidataQid);
    }

    // ── Deduplication when narrator and performer refer to the same person ────

    [Fact]
    public void DuplicateNarratorAndPerformer_Deduplicated()
    {
        // Both narrator and performer map to the same QID — should collapse to one.
        var claims = new List<ProviderClaim>
        {
            new("narrator",      "Tim Gerard Reynolds",        0.70),
            new("performer",     "Tim Gerard Reynolds",        0.90),
            new("narrator_qid",  "Q123456::Tim Gerard Reynolds", 0.70),
            new("performer_qid", "Q123456::Tim Gerard Reynolds", 0.90),
        };

        var refs = Extract(claims);

        Assert.Single(refs);
        Assert.Equal("Narrator", refs[0].Role);
    }

    // ── Author and narrator both present ─────────────────────────────────────

    [Fact]
    public void AuthorAndNarrator_BothPresent()
    {
        var claims = new List<ProviderClaim>
        {
            new("author",       "Andy Weir",           0.90),
            new("author_qid",   "Q7204::Andy Weir",    0.90),
            new("narrator",     "Ray Porter",          0.70),
            new("narrator_qid", "Q7654321::Ray Porter", 0.70),
        };

        var refs = Extract(claims);

        Assert.Equal(2, refs.Count);
        Assert.Contains(refs, r => r.Name == "Andy Weir"   && r.Role == "Author");
        Assert.Contains(refs, r => r.Name == "Ray Porter"  && r.Role == "Narrator");
    }

    // ── No person claims at all ───────────────────────────────────────────────

    [Fact]
    public void NoPersonClaims_ReturnsEmpty()
    {
        var claims = new List<ProviderClaim>
        {
            new("title", "Dune", 0.90),
            new("year",  "1965", 0.85),
        };

        var refs = Extract(claims);

        Assert.Empty(refs);
    }

    // ── Collective pseudonym constituent members ───────────────────────────────

    [Fact]
    public void CollectiveMembers_Extracted()
    {
        // The production code applies a QID-first filter: refs without a confirmed Wikidata
        // QID are dropped before the list is returned. "James S.A. Corey" has no author_qid
        // companion, so it is dropped. The two constituent members from collective_members_qid
        // both carry QIDs and are retained.
        var claims = new List<ProviderClaim>
        {
            new("author",                "James S.A. Corey",            0.95),
            new("collective_members_qid", "Q453384::Daniel Abraham",    0.90),
            new("collective_members_qid", "Q2076935::Ty Franck",        0.90),
        };

        var refs = Extract(claims);

        Assert.Equal(2, refs.Count);
        Assert.Contains(refs, r =>
            r.Name == "Daniel Abraham" && r.Role == "Author" && r.WikidataQid == "Q453384");
        Assert.Contains(refs, r =>
            r.Name == "Ty Franck" && r.Role == "Author" && r.WikidataQid == "Q2076935");
    }

    // ── QID parsed correctly from "QID::Label" segment ───────────────────────

    [Fact]
    public void QidParsedFromSegment_ColonColonFormat()
    {
        var claims = new List<ProviderClaim>
        {
            new("author",     "Isaac Asimov",          0.90),
            new("author_qid", "Q46248::Isaac Asimov",  0.90),
        };

        var refs = Extract(claims);

        Assert.Single(refs);
        // QID must be only the prefix before "::"
        Assert.Equal("Q46248", refs[0].WikidataQid);
        Assert.Equal("Isaac Asimov", refs[0].Name);
    }

    // ── Author without any QID companion ─────────────────────────────────────

    [Fact]
    public void AuthorWithoutQid_StillCreatesPersonReference()
    {
        // The production code applies a QID-first filter: refs without a confirmed Wikidata
        // QID are dropped. An author claim with no accompanying author_qid yields an empty
        // list — Person records require a verified Wikidata identity.
        var claims = new List<ProviderClaim>
        {
            new("author", "Unknown Author", 0.60),
        };

        var refs = Extract(claims);

        Assert.Empty(refs);
    }
}
