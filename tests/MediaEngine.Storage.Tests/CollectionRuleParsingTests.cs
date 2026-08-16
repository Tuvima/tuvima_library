using System.Text.Json;
using MediaEngine.Domain.Models;
using MediaEngine.Storage;

namespace MediaEngine.Storage.Tests;

public sealed class CollectionRuleParsingTests
{
    [Fact]
    public void ParseDefinition_AcceptsVersionedGroupedRules()
    {
        var definition = CollectionRuleEvaluator.ParseDefinition(
            """{"version":1,"groups":[{"id":"media","matchMode":"all","conditions":[{"field":"media_type","op":"eq","value":"Books"}]}]}""");

        var group = Assert.Single(definition.Groups);
        Assert.Equal("media", group.Id);
        Assert.Equal("all", group.MatchMode);
        var rule = Assert.Single(group.Conditions);
        Assert.Equal("media_type", rule.Field);
        Assert.Equal("eq", rule.Op);
        Assert.Equal("Books", rule.Value);
    }

    [Fact]
    public void ParseDefinition_RejectsRemovedUngroupedFormat()
    {
        var error = Assert.Throws<FormatException>(() =>
            CollectionRuleEvaluator.ParseDefinition(
                """{"genre":"Science Fiction","min":3,"media":"Any"}"""));

        Assert.Contains("versioned grouped", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ParseDefinition_PreservesMultiPersonAllOperatorAndDurableValues()
    {
        var definition = CollectionRuleEvaluator.ParseDefinition(
            """{"version":1,"groups":[{"id":"people","matchMode":"any","conditions":[{"field":"person_qid","op":"all_of","values":["Q25191","Q17714"],"displayValue":"Christopher Nolan + Matt Damon"}]}]}""");

        var group = Assert.Single(definition.Groups);
        Assert.Equal("any", group.MatchMode);
        var rule = Assert.Single(group.Conditions);
        Assert.Equal("all_of", rule.Op);
        Assert.Equal(["Q25191", "Q17714"], Assert.IsType<string[]>(rule.Values));
    }

    [Fact]
    public void RuleDefinition_SerializesOnlyTheVersionedGroupedShape()
    {
        var definition = CollectionRuleDefinition.SingleGroup(
            [new CollectionRulePredicate { Field = "media_type", Op = "eq", Value = "Movies" }]);

        var json = JsonSerializer.Serialize(definition);

        Assert.Contains("\"Groups\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("AllConditions", json, StringComparison.Ordinal);
    }
}
