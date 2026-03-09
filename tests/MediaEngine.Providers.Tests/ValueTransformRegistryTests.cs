using MediaEngine.Providers.Models;

namespace MediaEngine.Providers.Tests;

/// <summary>
/// Tests for the named value-transform registry used by config-driven adapters.
/// </summary>
public class ValueTransformRegistryTests
{
    // ════════════════════════════════════════════════════════════════════════
    //  Simple transforms (no args)
    // ════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("1965-06-01T00:00:00Z", "1965")]
    [InlineData("2023-12-25", "2023")]
    [InlineData("20", "20")]
    public void YearFromIso_ExtractsFourDigitYear(string input, string expected)
    {
        Assert.Equal(expected, ValueTransformRegistry.Apply("year_from_iso", input));
    }

    [Theory]
    [InlineData("Book 3", "3")]
    [InlineData("Part 12 of 20", "12")]
    [InlineData("42", "42")]
    [InlineData("3.5", "3.5")]
    public void NumericPortion_ExtractsFirstNumber(string input, string expected)
    {
        Assert.Equal(expected, ValueTransformRegistry.Apply("numeric_portion", input));
    }

    [Fact]
    public void StripEntityUri_RemovesWikidataPrefix()
    {
        var result = ValueTransformRegistry.Apply("strip_entity_uri",
            "http://www.wikidata.org/entity/Q83853");
        Assert.Equal("Q83853", result);
    }

    [Fact]
    public void StripEntityUri_PassesThroughNonUri()
    {
        Assert.Equal("Q83853", ValueTransformRegistry.Apply("strip_entity_uri", "Q83853"));
    }

    [Fact]
    public void CommonsUrl_BuildsThumbnailUrl()
    {
        var result = ValueTransformRegistry.Apply("commons_url", "Frank Herbert portrait.jpg");
        Assert.Contains("commons.wikimedia.org", result);
        Assert.Contains("Special:FilePath", result);
        Assert.Contains("width=300", result);
        Assert.Contains("Frank_Herbert_portrait.jpg", Uri.UnescapeDataString(result!));
    }

    [Fact]
    public void ToString_PassesThrough()
    {
        Assert.Equal("hello", ValueTransformRegistry.Apply("to_string", "hello"));
    }

    [Fact]
    public void StripHtml_RemovesTags()
    {
        var html = "<p>Hello <b>world</b>!</p>";
        var result = ValueTransformRegistry.Apply("strip_html", html);
        Assert.DoesNotContain("<", result);
        Assert.DoesNotContain(">", result);
        Assert.Contains("Hello", result);
        Assert.Contains("world", result);
    }

    [Fact]
    public void StripHtml_DecodesEntities()
    {
        var html = "A &amp; B &lt; C";
        var result = ValueTransformRegistry.Apply("strip_html", html);
        Assert.Contains("A & B < C", result);
    }

    [Fact]
    public void NullTransformName_ReturnsRawValue()
    {
        Assert.Equal("raw", ValueTransformRegistry.Apply(null, "raw"));
    }

    [Fact]
    public void UnknownTransformName_ReturnsRawValue()
    {
        Assert.Equal("raw", ValueTransformRegistry.Apply("nonexistent_transform", "raw"));
    }

    // ════════════════════════════════════════════════════════════════════════
    //  Parameterised transforms (with args)
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void UrlTemplate_SubstitutesValue()
    {
        var result = ValueTransformRegistry.Apply("url_template", "12345",
            "https://covers.openlibrary.org/b/id/{value}-L.jpg");
        Assert.Equal("https://covers.openlibrary.org/b/id/12345-L.jpg", result);
    }

    [Fact]
    public void UrlTemplate_NullArgs_ReturnsRaw()
    {
        Assert.Equal("12345", ValueTransformRegistry.Apply("url_template", "12345", null));
    }

    [Fact]
    public void RegexReplace_AppliesPattern()
    {
        var result = ValueTransformRegistry.Apply("regex_replace", "ISBN: 978-0-306-40615-7",
            @"\D+|");
        Assert.Equal("9780306406157", result);
    }

    [Fact]
    public void RegexReplace_NullArgs_ReturnsRaw()
    {
        Assert.Equal("abc", ValueTransformRegistry.Apply("regex_replace", "abc", null));
    }

    [Fact]
    public void FirstNChars_TruncatesLongString()
    {
        var result = ValueTransformRegistry.Apply("first_n_chars",
            "A very long description that goes on forever", "10");
        Assert.Equal("A very lon", result);
    }

    [Fact]
    public void FirstNChars_ShortString_ReturnsUnchanged()
    {
        var result = ValueTransformRegistry.Apply("first_n_chars", "Short", "100");
        Assert.Equal("Short", result);
    }

    [Fact]
    public void FirstNChars_InvalidArgs_ReturnsRaw()
    {
        Assert.Equal("data", ValueTransformRegistry.Apply("first_n_chars", "data", "not_a_number"));
    }

    // ════════════════════════════════════════════════════════════════════════
    //  IsKnown
    // ════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("year_from_iso")]
    [InlineData("numeric_portion")]
    [InlineData("strip_entity_uri")]
    [InlineData("commons_url")]
    [InlineData("to_string")]
    [InlineData("strip_html")]
    [InlineData("url_template")]
    [InlineData("regex_replace")]
    [InlineData("first_n_chars")]
    [InlineData("fallback_key")]
    [InlineData("prefer_isbn13")]
    [InlineData("array_join")]
    [InlineData("array_nested_join")]
    public void IsKnown_RecognisedTransforms(string name)
    {
        Assert.True(ValueTransformRegistry.IsKnown(name));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("nonexistent")]
    public void IsKnown_UnrecognisedTransforms(string? name)
    {
        Assert.False(ValueTransformRegistry.IsKnown(name));
    }

    [Fact]
    public void IsKnown_CaseInsensitive()
    {
        Assert.True(ValueTransformRegistry.IsKnown("YEAR_FROM_ISO"));
        Assert.True(ValueTransformRegistry.IsKnown("Strip_Html"));
    }

    // ════════════════════════════════════════════════════════════════════════
    //  Parameterised Apply falls back to simple
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ParameterisedApply_FallsBackToSimpleTransform()
    {
        // "to_string" only exists in simple registry, not args registry
        // But calling with 3 args should still find it via fallback
        var result = ValueTransformRegistry.Apply("to_string", "hello", "ignored_args");
        Assert.Equal("hello", result);
    }
}
