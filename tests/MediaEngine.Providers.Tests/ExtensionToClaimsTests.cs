using System.Reflection;
using MediaEngine.Providers.Adapters;
using MediaEngine.Providers.Models;
using Tuvima.WikidataReconciliation;

namespace MediaEngine.Providers.Tests;

/// <summary>
/// Tests for the <c>ExtensionToClaims</c> private static method in
/// <see cref="ReconciliationAdapter"/>.
///
/// Key fix verified here: companion <c>_qid</c> claims must carry a human-readable
/// label (e.g. "Q44413::Frank Herbert"), not a bare QID repeated as both halves
/// (e.g. "Q44413::Q44413").
/// </summary>
public sealed class ExtensionToClaimsTests
{
    private static readonly MethodInfo ExtensionToClaimsMethod = typeof(ReconciliationAdapter)
        .GetMethod("ExtensionToClaims", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException(
               "ExtensionToClaims method not found on ReconciliationAdapter");

    /// <summary>
    /// Helper: creates a WikidataClaim with an entity value (EntityId kind).
    /// </summary>
    private static WikidataClaim EntityClaim(string propertyId, string entityId, string? label = null) =>
        new()
        {
            PropertyId = propertyId,
            Rank = "normal",
            Value = new WikidataValue
            {
                Kind = WikidataValueKind.EntityId,
                RawValue = label ?? entityId,
                EntityId = entityId,
            }
        };

    /// <summary>
    /// Helper: creates a WikidataClaim with a string value.
    /// </summary>
    private static WikidataClaim StringClaim(string propertyId, string value) =>
        new()
        {
            PropertyId = propertyId,
            Rank = "normal",
            Value = new WikidataValue
            {
                Kind = WikidataValueKind.String,
                RawValue = value,
            }
        };

    /// <summary>
    /// Invokes <c>ExtensionToClaims</c> via reflection and materialises the result.
    /// </summary>
    private static List<ProviderClaim> ConvertToClaims(
        string entityQid,
        IReadOnlyDictionary<string, IReadOnlyList<WikidataClaim>> properties,
        Dictionary<string, string> propertyLabels,
        bool isWork = true)
    {
        var result = ExtensionToClaimsMethod.Invoke(null, [entityQid, properties, propertyLabels, isWork]);
        return ((IEnumerable<ProviderClaim>)result!).ToList();
    }

    // ── Companion _qid claims use human-readable label ────────────────────────

    [Fact]
    public void CompanionQidClaims_UseHumanReadableLabel()
    {
        // Arrange: entity value with both EntityId and label (RawValue).
        var properties = new Dictionary<string, IReadOnlyList<WikidataClaim>>
        {
            ["P50"] = [EntityClaim("P50", "Q44413", "Frank Herbert")],
        };
        var labels = new Dictionary<string, string> { ["P50"] = "author" };

        // Act
        var claims = ConvertToClaims("Q190159", properties, labels);

        // Assert: the author label claim carries the human-readable name.
        var authorClaim = claims.FirstOrDefault(c => c.Key == "author");
        Assert.NotNull(authorClaim);
        Assert.Equal("Frank Herbert", authorClaim.Value);

        // The companion _qid claim must be "QID::Label", not "QID::QID".
        var qidClaim = claims.FirstOrDefault(c => c.Key == "author_qid");
        Assert.NotNull(qidClaim);
        Assert.Equal("Q44413::Frank Herbert", qidClaim.Value);
    }

    [Fact]
    public void CompanionQidClaim_DoesNotRepeatQidAsLabel()
    {
        // Regression guard: before the fix, fallback was val.Id, producing "Q44413::Q44413".
        var properties = new Dictionary<string, IReadOnlyList<WikidataClaim>>
        {
            ["P50"] = [EntityClaim("P50", "Q44413", "Frank Herbert")],
        };
        var labels = new Dictionary<string, string> { ["P50"] = "author" };

        var claims = ConvertToClaims("Q190159", properties, labels);

        var qidClaim = claims.First(c => c.Key == "author_qid");
        Assert.NotEqual("Q44413::Q44413", qidClaim.Value);
    }

    // ── Label absent: falls back to QID ──────────────────────────────────────

    [Fact]
    public void CompanionQidClaim_FallsBackToQidWhenLabelIsNull()
    {
        // When label (RawValue) matches the EntityId, the QID itself is used as the label.
        var properties = new Dictionary<string, IReadOnlyList<WikidataClaim>>
        {
            ["P50"] = [EntityClaim("P50", "Q44413")],
        };
        var labels = new Dictionary<string, string> { ["P50"] = "author" };

        var claims = ConvertToClaims("Q190159", properties, labels);

        var qidClaim = claims.FirstOrDefault(c => c.Key == "author_qid");
        Assert.NotNull(qidClaim);
        // Should still include the QID on the left-hand side.
        Assert.StartsWith("Q44413::", qidClaim.Value);
    }

    // ── Multiple entity values → individual claims each ───────────────────────

    [Fact]
    public void MultiValuedEntityProperty_EmitsOneClaimPerValue()
    {
        var properties = new Dictionary<string, IReadOnlyList<WikidataClaim>>
        {
            ["P50"] =
            [
                EntityClaim("P50", "Q46248", "Terry Pratchett"),
                EntityClaim("P50", "Q210112", "Neil Gaiman"),
            ],
        };
        var labels = new Dictionary<string, string> { ["P50"] = "author" };

        var claims = ConvertToClaims("Q1234", properties, labels);

        var authorClaims = claims.Where(c => c.Key == "author").ToList();
        Assert.Equal(2, authorClaims.Count);
        Assert.Contains(authorClaims, c => c.Value == "Terry Pratchett");
        Assert.Contains(authorClaims, c => c.Value == "Neil Gaiman");
    }

    [Fact]
    public void MultiValuedEntityProperty_EmitsCompanionQidPerValue()
    {
        var properties = new Dictionary<string, IReadOnlyList<WikidataClaim>>
        {
            ["P50"] =
            [
                EntityClaim("P50", "Q46248", "Terry Pratchett"),
                EntityClaim("P50", "Q210112", "Neil Gaiman"),
            ],
        };
        var labels = new Dictionary<string, string> { ["P50"] = "author" };

        var claims = ConvertToClaims("Q1234", properties, labels);

        var qidClaims = claims.Where(c => c.Key == "author_qid").ToList();
        Assert.Equal(2, qidClaims.Count);
        Assert.Contains(qidClaims, c => c.Value == "Q46248::Terry Pratchett");
        Assert.Contains(qidClaims, c => c.Value == "Q210112::Neil Gaiman");
    }

    // ── P18 guard: image property suppressed for Work entities ────────────────

    [Fact]
    public void P18Image_SuppressedForWorkEntities()
    {
        var properties = new Dictionary<string, IReadOnlyList<WikidataClaim>>
        {
            ["P18"] = [StringClaim("P18", "Frank_Herbert.jpg")],
        };
        // Map P18 → headshot_url (as in the real provider config).
        var labels = new Dictionary<string, string> { ["P18"] = "headshot_url" };

        var claims = ConvertToClaims("Q190159", properties, labels, isWork: true);

        // P18 is not emitted for Work entities.
        Assert.DoesNotContain(claims, c => c.Key == "headshot_url");
    }

    [Fact]
    public void P18Image_EmittedForPersonEntities()
    {
        var properties = new Dictionary<string, IReadOnlyList<WikidataClaim>>
        {
            ["P18"] = [StringClaim("P18", "Frank_Herbert.jpg")],
        };
        var labels = new Dictionary<string, string> { ["P18"] = "headshot_url" };

        var claims = ConvertToClaims("Q44413", properties, labels, isWork: false);

        // P18 is converted to a Wikimedia Commons URL for Person entities.
        var headshotClaim = claims.FirstOrDefault(c => c.Key == "headshot_url");
        Assert.NotNull(headshotClaim);
        Assert.Contains("commons.wikimedia.org", headshotClaim.Value);
        Assert.Contains("Frank_Herbert.jpg", headshotClaim.Value);
    }

    // ── Unknown property code → no claim emitted ─────────────────────────────

    [Fact]
    public void UnknownPropertyCode_SkippedSilently()
    {
        // P999 is not in the labels dictionary.
        var properties = new Dictionary<string, IReadOnlyList<WikidataClaim>>
        {
            ["P999"] = [StringClaim("P999", "some value")],
        };
        var labels = new Dictionary<string, string>(); // empty — P999 unknown

        var claims = ConvertToClaims("Q190159", properties, labels);

        Assert.Empty(claims);
    }
}
