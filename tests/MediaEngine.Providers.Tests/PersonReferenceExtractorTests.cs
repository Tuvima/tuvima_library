using MediaEngine.Domain;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;
using MediaEngine.Providers.Helpers;
using MediaEngine.Providers.Models;

namespace MediaEngine.Providers.Tests;

/// <summary>
/// Tests for <see cref="PersonReferenceExtractor"/> — static person reference
/// extraction from raw claims and canonical values.
/// </summary>
public sealed class PersonReferenceExtractorTests
{
    private static readonly Guid TestEntity = Guid.NewGuid();

    // ── FromRawClaims: QID-first refs ───────────────────────────────────────

    [Fact]
    public void FromRawClaims_AuthorWithQid_ReturnsReference()
    {
        var claims = new List<ProviderClaim>
        {
            new(MetadataFieldConstants.Author, "Frank Herbert", 0.90),
            new("author_qid", "Q44413::Frank Herbert", 0.90),
        };

        var refs = PersonReferenceExtractor.FromRawClaims(claims);

        Assert.Single(refs);
        Assert.Equal("Author", refs[0].Role);
        Assert.Equal("Frank Herbert", refs[0].Name);
        Assert.Equal("Q44413", refs[0].WikidataQid);
    }

    [Fact]
    public void FromRawClaims_AuthorWithoutQid_ExcludedFromResults()
    {
        var claims = new List<ProviderClaim>
        {
            new(MetadataFieldConstants.Author, "Unknown Author", 0.90),
        };

        var refs = PersonReferenceExtractor.FromRawClaims(claims);

        Assert.Empty(refs);
    }

    [Fact]
    public void FromRawClaims_MultipleRoles_AllExtracted()
    {
        var claims = new List<ProviderClaim>
        {
            new(MetadataFieldConstants.Author, "Frank Herbert", 0.90),
            new("author_qid", "Q44413::Frank Herbert", 0.90),
            new("director", "Denis Villeneuve", 0.85),
            new("director_qid", "Q381178::Denis Villeneuve", 0.85),
        };

        var refs = PersonReferenceExtractor.FromRawClaims(claims);

        Assert.Equal(2, refs.Count);
        Assert.Contains(refs, r => r.Role == "Author" && r.WikidataQid == "Q44413");
        Assert.Contains(refs, r => r.Role == "Director" && r.WikidataQid == "Q381178");
    }

    [Fact]
    public void FromRawClaims_DuplicateQids_Deduplicated()
    {
        var claims = new List<ProviderClaim>
        {
            new(MetadataFieldConstants.Author, "Frank Herbert", 0.90),
            new("author_qid", "Q44413::Frank Herbert", 0.90),
            new(MetadataFieldConstants.Author, "Frank Herbert", 0.85),
            new("author_qid", "Q44413::Frank Herbert", 0.85),
        };

        var refs = PersonReferenceExtractor.FromRawClaims(claims);

        Assert.Single(refs);
    }

    [Fact]
    public void FromRawClaims_SameQidDifferentRoles_PreservesEachRole()
    {
        var claims = new List<ProviderClaim>
        {
            new("director", "Bryan Cranston", 0.90),
            new("director_qid", "Q94687::Bryan Cranston", 0.90),
            new("cast_member", "Bryan Cranston", 0.90),
            new("cast_member_qid", "Q94687::Bryan Cranston", 0.90),
        };

        var refs = PersonReferenceExtractor.FromRawClaims(claims, MediaType.Movies);

        Assert.Equal(2, refs.Count);
        Assert.Contains(refs, r => r.Role == "Director" && r.WikidataQid == "Q94687");
        Assert.Contains(refs, r => r.Role == "Actor" && r.WikidataQid == "Q94687");
    }

    [Fact]
    public void FromRawClaims_CollectivePseudonym_FlagSet()
    {
        var claims = new List<ProviderClaim>
        {
            new(MetadataFieldConstants.Author, "James S.A. Corey", 0.90),
            new("author_qid", "Q6142591::James S.A. Corey", 0.90),
            new("author_is_collective_pseudonym", "true", 0.90),
        };

        var refs = PersonReferenceExtractor.FromRawClaims(claims);

        Assert.Single(refs);
        Assert.True(refs[0].IsCollectivePseudonym);
    }

    [Fact]
    public void FromRawClaims_CollectiveMembers_NotAddedAsDirectAuthors()
    {
        var claims = new List<ProviderClaim>
        {
            new(MetadataFieldConstants.Author, "James S.A. Corey", 0.90),
            new("author_qid", "Q6142591::James S.A. Corey", 0.90),
            new("author_is_collective_pseudonym", "true", 0.90),
            new("collective_members_qid", "Q123456::Daniel Abraham", 0.90),
            new("collective_members_qid", "Q789012::Ty Franck", 0.90),
        };

        var refs = PersonReferenceExtractor.FromRawClaims(claims);

        Assert.Single(refs);
        Assert.Contains(refs, r => r.Name == "James S.A. Corey" && r.WikidataQid == "Q6142591");
        Assert.DoesNotContain(refs, r => r.WikidataQid == "Q123456");
        Assert.DoesNotContain(refs, r => r.WikidataQid == "Q789012");
    }

    // ── FromRawClaims: performer role mapping ───────────────────────────────

    [Fact]
    public void FromRawClaims_MusicPerformer_GetsPerformerRole()
    {
        var claims = new List<ProviderClaim>
        {
            new("performer", "The Beatles", 0.90),
            new("performer_qid", "Q1299::The Beatles", 0.90),
        };

        var refs = PersonReferenceExtractor.FromRawClaims(claims, MediaType.Music);

        Assert.Single(refs);
        Assert.Equal("Performer", refs[0].Role);
    }

    [Fact]
    public void FromRawClaims_AudiobookPerformer_GetsNarratorRole()
    {
        var claims = new List<ProviderClaim>
        {
            new("performer", "Stephen Fry", 0.90),
            new("performer_qid", "Q192640::Stephen Fry", 0.90),
        };

        var refs = PersonReferenceExtractor.FromRawClaims(claims, MediaType.Audiobooks);

        Assert.Single(refs);
        Assert.Equal("Narrator", refs[0].Role);
    }

    [Fact]
    public void FromRawClaims_MoviePerformer_GetsActorRole()
    {
        var claims = new List<ProviderClaim>
        {
            new("performer", "Timothée Chalamet", 0.90),
            new("performer_qid", "Q506176::Timothée Chalamet", 0.90),
        };

        var refs = PersonReferenceExtractor.FromRawClaims(claims, MediaType.Movies);

        Assert.Single(refs);
        Assert.Equal("Actor", refs[0].Role);
    }

    // ── FromRawClaimsUnlinked: names without QIDs ───────────────────────────

    [Fact]
    public void FromRawClaimsUnlinked_NameWithoutQid_Returned()
    {
        var claims = new List<ProviderClaim>
        {
            new(MetadataFieldConstants.Author, "Brandon Sanderson", 0.90),
        };

        var refs = PersonReferenceExtractor.FromRawClaimsUnlinked(claims);

        Assert.Single(refs);
        Assert.Equal("Author", refs[0].Role);
        Assert.Equal("Brandon Sanderson", refs[0].Name);
        Assert.Null(refs[0].WikidataQid);
    }

    [Fact]
    public void FromRawClaimsUnlinked_NameWithQid_Excluded()
    {
        var claims = new List<ProviderClaim>
        {
            new(MetadataFieldConstants.Author, "Frank Herbert", 0.90),
            new("author_qid", "Q44413::Frank Herbert", 0.90),
        };

        var refs = PersonReferenceExtractor.FromRawClaimsUnlinked(claims);

        Assert.Empty(refs);
    }

    [Fact]
    public void FromRawClaimsUnlinked_DuplicateNames_Deduplicated()
    {
        var claims = new List<ProviderClaim>
        {
            new(MetadataFieldConstants.Author, "Brandon Sanderson", 0.90),
            new(MetadataFieldConstants.Author, "Brandon Sanderson", 0.85),
        };

        var refs = PersonReferenceExtractor.FromRawClaimsUnlinked(claims);

        Assert.Single(refs);
    }

    // ── FromCanonicals ──────────────────────────────────────────────────────

    [Fact]
    public void FromCanonicals_AuthorWithQid_Extracted()
    {
        var canonicals = new List<CanonicalValue>
        {
            new() { EntityId = TestEntity, Key = MetadataFieldConstants.Author, Value = "Frank Herbert" },
            new() { EntityId = TestEntity, Key = "author_qid", Value = "Q44413::Frank Herbert" },
        };

        var refs = PersonReferenceExtractor.FromCanonicals(canonicals);

        Assert.Single(refs);
        Assert.Equal("Author", refs[0].Role);
        Assert.Equal("Q44413", refs[0].WikidataQid);
    }

    [Fact]
    public void FromCanonicals_MultiValuedAuthors_SplitOnTriplePipe()
    {
        var canonicals = new List<CanonicalValue>
        {
            new() { EntityId = TestEntity, Key = MetadataFieldConstants.Author, Value = "Neil Gaiman|||Terry Pratchett" },
            new() { EntityId = TestEntity, Key = "author_qid", Value = "Q210112::Neil Gaiman|||Q46248::Terry Pratchett" },
        };

        var refs = PersonReferenceExtractor.FromCanonicals(canonicals);

        Assert.Equal(2, refs.Count);
        Assert.Contains(refs, r => r.Name == "Neil Gaiman" && r.WikidataQid == "Q210112");
        Assert.Contains(refs, r => r.Name == "Terry Pratchett" && r.WikidataQid == "Q46248");
    }

    [Fact]
    public void FromCanonicals_PerformerRole_MediaTypeAware()
    {
        var canonicals = new List<CanonicalValue>
        {
            new() { EntityId = TestEntity, Key = MetadataFieldConstants.Artist, Value = "Radiohead" },
            new() { EntityId = TestEntity, Key = "artist_qid", Value = "Q44190::Radiohead" },
        };

        var refs = PersonReferenceExtractor.FromCanonicals(canonicals, MediaType.Music);

        Assert.Single(refs);
        Assert.Equal("Performer", refs[0].Role);
    }

    [Fact]
    public void FromCanonicals_NullAuthorValue_NoResults()
    {
        var canonicals = new List<CanonicalValue>
        {
            new() { EntityId = TestEntity, Key = MetadataFieldConstants.Author, Value = "" },
        };

        var refs = PersonReferenceExtractor.FromCanonicals(canonicals);

        Assert.Empty(refs);
    }

    [Fact]
    public void FromCanonicals_CollectiveMembers_NotAddedAsDirectAuthors()
    {
        var canonicals = new List<CanonicalValue>
        {
            new() { EntityId = TestEntity, Key = MetadataFieldConstants.Author, Value = "James S.A. Corey" },
            new() { EntityId = TestEntity, Key = "author_qid", Value = "Q6142591::James S.A. Corey" },
            new() { EntityId = TestEntity, Key = "collective_members_qid", Value = "Q123456::Daniel Abraham|||Q789012::Ty Franck" },
        };

        var refs = PersonReferenceExtractor.FromCanonicals(canonicals);

        Assert.Single(refs);
        Assert.Contains(refs, r => r.WikidataQid == "Q6142591");
        Assert.DoesNotContain(refs, r => r.WikidataQid == "Q123456");
        Assert.DoesNotContain(refs, r => r.WikidataQid == "Q789012");
    }

    [Fact]
    public void FromRawClaims_MismatchedAuthorNamesAndQids_UsesQidLabels()
    {
        var claims = new List<ProviderClaim>
        {
            new(MetadataFieldConstants.Author, "James S.A. Corey", 0.90),
            new(MetadataFieldConstants.Author, "James S. A. Corey", 0.90),
            new(MetadataFieldConstants.Author, "James S. A. Corey", 0.90),
            new("author_qid", "Q6142591::James S. A. Corey", 0.90),
            new("author_qid", "Q1159871::Daniel Abraham", 0.90),
            new("author_qid", "Q18608460::Ty Franck", 0.90),
        };

        var refs = PersonReferenceExtractor.FromRawClaims(claims);

        Assert.Contains(refs, r => r.WikidataQid == "Q6142591" && r.Name == "James S. A. Corey");
        Assert.Contains(refs, r => r.WikidataQid == "Q1159871" && r.Name == "Daniel Abraham");
        Assert.Contains(refs, r => r.WikidataQid == "Q18608460" && r.Name == "Ty Franck");
        Assert.DoesNotContain(refs, r => r.WikidataQid == "Q18608460" && r.Name == "James S. A. Corey");
    }
}
