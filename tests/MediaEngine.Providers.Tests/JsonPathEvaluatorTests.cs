using System.Text.Json.Nodes;
using MediaEngine.Providers.Models;

namespace MediaEngine.Providers.Tests;

/// <summary>
/// Tests for the JSON path evaluation utility used by config-driven adapters.
/// </summary>
public class JsonPathEvaluatorTests
{
    // ════════════════════════════════════════════════════════════════════════
    //  Evaluate — property access
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Evaluate_SimpleProperty()
    {
        var json = JsonNode.Parse("""{"title": "Dune"}""");
        var result = JsonPathEvaluator.Evaluate(json, "title");

        Assert.Equal("Dune", result?.GetValue<string>());
    }

    [Fact]
    public void Evaluate_NestedProperty()
    {
        var json = JsonNode.Parse("""{"volumeInfo": {"title": "Dune", "pageCount": 412}}""");
        var result = JsonPathEvaluator.Evaluate(json, "volumeInfo.title");

        Assert.Equal("Dune", result?.GetValue<string>());
    }

    [Fact]
    public void Evaluate_DeeplyNested()
    {
        var json = JsonNode.Parse("""{"a": {"b": {"c": {"d": "deep"}}}}""");
        var result = JsonPathEvaluator.Evaluate(json, "a.b.c.d");

        Assert.Equal("deep", result?.GetValue<string>());
    }

    [Fact]
    public void Evaluate_MissingProperty_ReturnsNull()
    {
        var json = JsonNode.Parse("""{"title": "Dune"}""");
        var result = JsonPathEvaluator.Evaluate(json, "author");

        Assert.Null(result);
    }

    [Fact]
    public void Evaluate_NullRoot_ReturnsNull()
    {
        Assert.Null(JsonPathEvaluator.Evaluate(null, "title"));
    }

    [Fact]
    public void Evaluate_EmptyPath_ReturnsNull()
    {
        var json = JsonNode.Parse("""{"title": "Dune"}""");
        Assert.Null(JsonPathEvaluator.Evaluate(json, ""));
    }

    // ════════════════════════════════════════════════════════════════════════
    //  Evaluate — array indexing
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Evaluate_ArrayIndex()
    {
        var json = JsonNode.Parse("""{"authors": ["Frank Herbert", "Brian Herbert"]}""");
        var result = JsonPathEvaluator.Evaluate(json, "authors[0]");

        Assert.Equal("Frank Herbert", result?.GetValue<string>());
    }

    [Fact]
    public void Evaluate_ArrayIndex_SecondElement()
    {
        var json = JsonNode.Parse("""{"items": ["a", "b", "c"]}""");
        var result = JsonPathEvaluator.Evaluate(json, "items[2]");

        Assert.Equal("c", result?.GetValue<string>());
    }

    [Fact]
    public void Evaluate_ArrayIndex_OutOfBounds_ReturnsNull()
    {
        var json = JsonNode.Parse("""{"items": ["a"]}""");
        Assert.Null(JsonPathEvaluator.Evaluate(json, "items[5]"));
    }

    [Fact]
    public void Evaluate_ArrayIndex_NegativeIndex_ReturnsNull()
    {
        var json = JsonNode.Parse("""{"items": ["a"]}""");
        Assert.Null(JsonPathEvaluator.Evaluate(json, "items[-1]"));
    }

    // ════════════════════════════════════════════════════════════════════════
    //  Evaluate — wildcard [*]
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Evaluate_Wildcard_ExtractsChildProperty()
    {
        var json = JsonNode.Parse("""
        {
            "narrators": [
                {"name": "Scott Brick"},
                {"name": "Simon Vance"}
            ]
        }
        """);

        var result = JsonPathEvaluator.Evaluate(json, "narrators[*].name");

        Assert.NotNull(result);
        Assert.True(JsonPathEvaluator.IsArray(result));
        var values = JsonPathEvaluator.GetArrayValues(result);
        Assert.Equal(2, values.Count);
        Assert.Contains("Scott Brick", values);
        Assert.Contains("Simon Vance", values);
    }

    [Fact]
    public void Evaluate_Wildcard_EmptyArray_ReturnsNull()
    {
        var json = JsonNode.Parse("""{"items": []}""");
        Assert.Null(JsonPathEvaluator.Evaluate(json, "items[*].name"));
    }

    // ════════════════════════════════════════════════════════════════════════
    //  GetStringValue
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void GetStringValue_String()
    {
        var node = JsonValue.Create("hello");
        Assert.Equal("hello", JsonPathEvaluator.GetStringValue(node));
    }

    [Fact]
    public void GetStringValue_Integer()
    {
        var node = JsonValue.Create(42);
        Assert.Equal("42", JsonPathEvaluator.GetStringValue(node));
    }

    [Fact]
    public void GetStringValue_Boolean()
    {
        var node = JsonValue.Create(true);
        Assert.Equal("True", JsonPathEvaluator.GetStringValue(node));
    }

    [Fact]
    public void GetStringValue_Null_ReturnsNull()
    {
        Assert.Null(JsonPathEvaluator.GetStringValue(null));
    }

    [Fact]
    public void GetStringValue_Array_ReturnsNull()
    {
        var node = new JsonArray(JsonValue.Create("a"), JsonValue.Create("b"));
        Assert.Null(JsonPathEvaluator.GetStringValue(node));
    }

    [Fact]
    public void GetStringValue_Object_ReturnsNull()
    {
        var node = new JsonObject { ["key"] = "value" };
        Assert.Null(JsonPathEvaluator.GetStringValue(node));
    }

    // ════════════════════════════════════════════════════════════════════════
    //  GetArrayValues
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void GetArrayValues_ExtractsStrings()
    {
        var arr = new JsonArray(JsonValue.Create("a"), JsonValue.Create("b"), JsonValue.Create("c"));
        var result = JsonPathEvaluator.GetArrayValues(arr);

        Assert.Equal(3, result.Count);
        Assert.Equal(["a", "b", "c"], result);
    }

    [Fact]
    public void GetArrayValues_SkipsNullAndWhitespace()
    {
        var arr = new JsonArray(JsonValue.Create("a"), null, JsonValue.Create(""), JsonValue.Create("b"));
        var result = JsonPathEvaluator.GetArrayValues(arr);

        Assert.Equal(2, result.Count);
        Assert.Equal(["a", "b"], result);
    }

    [Fact]
    public void GetArrayValues_NonArray_ReturnsEmpty()
    {
        var node = JsonValue.Create("not an array");
        Assert.Empty(JsonPathEvaluator.GetArrayValues(node));
    }

    // ════════════════════════════════════════════════════════════════════════
    //  IsArray
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void IsArray_True_ForJsonArray()
    {
        Assert.True(JsonPathEvaluator.IsArray(new JsonArray()));
    }

    [Fact]
    public void IsArray_False_ForValue()
    {
        Assert.False(JsonPathEvaluator.IsArray(JsonValue.Create("str")));
    }

    [Fact]
    public void IsArray_False_ForNull()
    {
        Assert.False(JsonPathEvaluator.IsArray(null));
    }
}
