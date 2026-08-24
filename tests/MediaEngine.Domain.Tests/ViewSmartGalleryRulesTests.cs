using MediaEngine.Domain.Models;
using MediaEngine.Domain.PersonalMedia;

namespace MediaEngine.Domain.Tests;

public sealed class ViewSmartGalleryRulesTests
{
    [Fact]
    public void ParsesVersionOneSnakeCaseGroupsAndNormalizesChoices()
    {
        var definition = ViewSmartGalleryRules.Parse("""
            {
              "version": 1,
              "groups": [
                {
                  "id": "photos",
                  "join_with_previous": "OR",
                  "match_mode": "ALL",
                  "conditions": [
                    { "field": "MEDIA_TYPE", "op": "EQ", "value": "image" },
                    { "field": "captured_date", "op": "between", "values": ["2026-01-01", "2026-12-31"] }
                  ]
                }
              ]
            }
            """);

        var group = Assert.Single(definition.Groups);
        Assert.Equal("all", group.MatchMode);
        Assert.Equal("or", group.JoinWithPrevious);
        Assert.Equal("media_type", group.Conditions[0].Field);
        Assert.Equal("eq", group.Conditions[0].Op);
    }

    [Theory]
    [InlineData("{\"version\":2,\"groups\":[{\"match_mode\":\"all\",\"join_with_previous\":\"or\",\"conditions\":[{\"field\":\"favorite\",\"op\":\"eq\",\"value\":\"true\"}]}]}")]
    [InlineData("{\"version\":1,\"groups\":[{\"match_mode\":\"all\",\"join_with_previous\":\"or\",\"conditions\":[{\"field\":\"title); DROP TABLE local_items;--\",\"op\":\"eq\",\"value\":\"x\"}]}]}")]
    [InlineData("{\"version\":1,\"groups\":[{\"match_mode\":\"all\",\"join_with_previous\":\"or\",\"conditions\":[{\"field\":\"media_type\",\"op\":\"like\",\"value\":\"%\"}]}]}")]
    [InlineData("{\"version\":1,\"groups\":[{\"match_mode\":\"all\",\"join_with_previous\":\"or\",\"conditions\":[{\"field\":\"captured_date\",\"op\":\"eq\",\"value\":\"08/23/2026\"}]}]}")]
    [InlineData("{not-json")]
    public void RejectsUnsupportedOrMalformedRules(string json)
    {
        Assert.Throws<ArgumentException>(() => ViewSmartGalleryRules.Parse(json));
    }

    [Fact]
    public void AcceptsEverySupportedViewFieldWithApplicableOperators()
    {
        var conditions = new[]
        {
            Rule("media_type", "in", values: ["image", "video"]),
            Rule("file_type", "contains", "jpeg"),
            Rule("orientation", "neq", "square"),
            Rule("duration", "gte", "12.5"),
            Rule("captured_date", "lt", "2026-08-24"),
            Rule("people", "known"),
            Rule("place", "unknown"),
            Rule("device", "eq", "phone"),
            Rule("tags", "in", values: ["family", "travel"]),
            Rule("favorite", "eq", "true"),
            Rule("owner", "contains", "shy"),
            Rule("source", "neq", "imports"),
        };
        var definition = CollectionRuleDefinition.SingleGroup(conditions);

        ViewSmartGalleryRules.Validate(definition);

        Assert.Equal(conditions.Length, definition.AllConditions.Count);
    }

    private static CollectionRulePredicate Rule(string field, string op, string? value = null, string[]? values = null) =>
        new() { Field = field, Op = op, Value = value, Values = values };
}
