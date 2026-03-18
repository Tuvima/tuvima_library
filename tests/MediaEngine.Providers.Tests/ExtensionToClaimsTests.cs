using System.Reflection;
using MediaEngine.Providers.Adapters;
using MediaEngine.Providers.Models;

namespace MediaEngine.Providers.Tests;

/// <summary>
/// Tests for the <c>ExtensionToClaims</c> private static method in
/// <see cref="ReconciliationAdapter"/>.
///
/// Key fix verified here: companion <c>_qid</c> claims must carry a human-readable
/// label (e.g. "Q44413::Frank Herbert"), not a bare QID repeated as both halves
/// (e.g. "Q44413::Q44413").  This regression was introduced when the label fallback
/// was erroneously set to <c>val.Id</c> rather than <c>val.Str ?? val.Id</c>.
/// </summary>
public sealed class ExtensionToClaimsTests
{
    private static readonly MethodInfo ExtensionToClaimsMethod = typeof(ReconciliationAdapter)
        .GetMethod("ExtensionToClaims", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException(
               "ExtensionToClaims method not found on ReconciliationAdapter");

    /// <summary>
    /// Invokes <c>ExtensionToClaims</c> via reflection and materialises the result.
    /// </summary>
    private static List<ProviderClaim> ConvertToClaims(
        ExtensionResult ext,
        Dictionary<string, string> propertyLabels,
        bool isWork = true)
    {
        var result = ExtensionToClaimsMethod.Invoke(null, [ext, propertyLabels, isWork]);
        return ((IEnumerable<ProviderClaim>)result!).ToList();
    }

    // ── Companion _qid claims use human-readable label ────────────────────────

    [Fact]
    public void CompanionQidClaims_UseHumanReadableLabel()
    {
        // Arrange: entity value with both Id and Label set.
        var ext = new ExtensionResult("Q190159", new Dictionary<string, List<ExtensionValue>>
        {
            ["P50"] = [new ExtensionValue(null, "Q44413", "Frank Herbert", null, null)],
        });
        var labels = new Dictionary<string, string> { ["P50"] = "author" };

        // Act
        var claims = ConvertToClaims(ext, labels);

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
        var ext = new ExtensionResult("Q190159", new Dictionary<string, List<ExtensionValue>>
        {
            ["P50"] = [new ExtensionValue(null, "Q44413", "Frank Herbert", null, null)],
        });
        var labels = new Dictionary<string, string> { ["P50"] = "author" };

        var claims = ConvertToClaims(ext, labels);

        var qidClaim = claims.First(c => c.Key == "author_qid");
        Assert.NotEqual("Q44413::Q44413", qidClaim.Value);
    }

    // ── Label absent: falls back to QID ──────────────────────────────────────

    [Fact]
    public void CompanionQidClaim_FallsBackToQidWhenLabelIsNull()
    {
        // When Label is null and Str is also null, the QID itself is used as the label.
        var ext = new ExtensionResult("Q190159", new Dictionary<string, List<ExtensionValue>>
        {
            ["P50"] = [new ExtensionValue(null, "Q44413", null, null, null)],
        });
        var labels = new Dictionary<string, string> { ["P50"] = "author" };

        var claims = ConvertToClaims(ext, labels);

        var qidClaim = claims.FirstOrDefault(c => c.Key == "author_qid");
        Assert.NotNull(qidClaim);
        // Should still include the QID on the left-hand side.
        Assert.StartsWith("Q44413::", qidClaim.Value);
    }

    // ── Multiple entity values → individual claims each ───────────────────────

    [Fact]
    public void MultiValuedEntityProperty_EmitsOneClaimPerValue()
    {
        var ext = new ExtensionResult("Q1234", new Dictionary<string, List<ExtensionValue>>
        {
            ["P50"] =
            [
                new ExtensionValue(null, "Q46248",  "Terry Pratchett", null, null),
                new ExtensionValue(null, "Q210112", "Neil Gaiman",     null, null),
            ],
        });
        var labels = new Dictionary<string, string> { ["P50"] = "author" };

        var claims = ConvertToClaims(ext, labels);

        var authorClaims = claims.Where(c => c.Key == "author").ToList();
        Assert.Equal(2, authorClaims.Count);
        Assert.Contains(authorClaims, c => c.Value == "Terry Pratchett");
        Assert.Contains(authorClaims, c => c.Value == "Neil Gaiman");
    }

    [Fact]
    public void MultiValuedEntityProperty_EmitsCompanionQidPerValue()
    {
        var ext = new ExtensionResult("Q1234", new Dictionary<string, List<ExtensionValue>>
        {
            ["P50"] =
            [
                new ExtensionValue(null, "Q46248",  "Terry Pratchett", null, null),
                new ExtensionValue(null, "Q210112", "Neil Gaiman",     null, null),
            ],
        });
        var labels = new Dictionary<string, string> { ["P50"] = "author" };

        var claims = ConvertToClaims(ext, labels);

        var qidClaims = claims.Where(c => c.Key == "author_qid").ToList();
        Assert.Equal(2, qidClaims.Count);
        Assert.Contains(qidClaims, c => c.Value == "Q46248::Terry Pratchett");
        Assert.Contains(qidClaims, c => c.Value == "Q210112::Neil Gaiman");
    }

    // ── P18 guard: image property suppressed for Work entities ────────────────

    [Fact]
    public void P18Image_SuppressedForWorkEntities()
    {
        var ext = new ExtensionResult("Q190159", new Dictionary<string, List<ExtensionValue>>
        {
            ["P18"] = [new ExtensionValue("Frank_Herbert.jpg", null, null, null, null)],
        });
        // Map P18 → headshot_url (as in the real provider config).
        var labels = new Dictionary<string, string> { ["P18"] = "headshot_url" };

        var claims = ConvertToClaims(ext, labels, isWork: true);

        // P18 is not emitted for Work entities.
        Assert.DoesNotContain(claims, c => c.Key == "headshot_url");
    }

    [Fact]
    public void P18Image_EmittedForPersonEntities()
    {
        var ext = new ExtensionResult("Q44413", new Dictionary<string, List<ExtensionValue>>
        {
            ["P18"] = [new ExtensionValue("Frank_Herbert.jpg", null, null, null, null)],
        });
        var labels = new Dictionary<string, string> { ["P18"] = "headshot_url" };

        var claims = ConvertToClaims(ext, labels, isWork: false);

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
        var ext = new ExtensionResult("Q190159", new Dictionary<string, List<ExtensionValue>>
        {
            ["P999"] = [new ExtensionValue("some value", null, null, null, null)],
        });
        var labels = new Dictionary<string, string>(); // empty — P999 unknown

        var claims = ConvertToClaims(ext, labels);

        Assert.Empty(claims);
    }
}
