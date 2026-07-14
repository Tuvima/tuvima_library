using MediaEngine.Storage;

namespace MediaEngine.Storage.Tests;

public sealed class CollectionRuleParsingTests
{
    [Fact]
    public void ParseRules_AcceptsCurrentPredicateArray()
    {
        var rules = CollectionRuleEvaluator.ParseRules(
            """[{"field":"media_type","op":"eq","value":"Books"}]""");

        var rule = Assert.Single(rules);
        Assert.Equal("media_type", rule.Field);
        Assert.Equal("eq", rule.Op);
        Assert.Equal("Books", rule.Value);
    }

    [Fact]
    public void ParseRules_RejectsRemovedObjectFormat()
    {
        var error = Assert.Throws<FormatException>(() =>
            CollectionRuleEvaluator.ParseRules(
                """{"genre":"Science Fiction","min":3,"media":"Any"}"""));

        Assert.Contains("JSON array", error.Message, StringComparison.Ordinal);
    }
}
