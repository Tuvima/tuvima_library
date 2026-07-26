using MediaEngine.Domain.Services;

namespace MediaEngine.Domain.Tests;

public sealed class StringHelpersTests
{
    [Fact]
    public void FirstNonBlank_ReturnsFirstNonBlankValue_InOrder()
    {
        Assert.Equal("b", StringHelpers.FirstNonBlank(null, "  ", "b", "c"));
    }

    [Fact]
    public void FirstNonBlank_ReturnsNull_WhenAllValuesAreNullOrWhitespace()
    {
        Assert.Null(StringHelpers.FirstNonBlank(null, "", "   ", null));
    }

    [Fact]
    public void FirstNonBlank_ReturnsNull_WhenNoValuesGiven()
    {
        Assert.Null(StringHelpers.FirstNonBlank());
    }

    [Fact]
    public void FirstNonBlankOr_ReturnsFirstNonBlankValue_WhenPresent()
    {
        Assert.Equal("value", StringHelpers.FirstNonBlankOr("fallback", null, "", "value"));
    }

    [Fact]
    public void FirstNonBlankOr_ReturnsFallback_WhenAllValuesAreNullOrWhitespace()
    {
        Assert.Equal("fallback", StringHelpers.FirstNonBlankOr("fallback", null, "  "));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankToNull_ReturnsNull_ForNullOrWhitespace(string? value)
    {
        Assert.Null(StringHelpers.BlankToNull(value));
    }

    [Fact]
    public void BlankToNull_ReturnsValueUnchanged_WhenNotBlank()
    {
        Assert.Equal("  padded  ", StringHelpers.BlankToNull("  padded  "));
    }
}
