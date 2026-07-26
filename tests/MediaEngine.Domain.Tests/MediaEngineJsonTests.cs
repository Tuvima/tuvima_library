using System.Text.Json;
using MediaEngine.Domain.Services;

namespace MediaEngine.Domain.Tests;

public sealed class MediaEngineJsonTests
{
    [Fact]
    public void Web_UsesCamelCasePropertyNaming()
    {
        var json = JsonSerializer.Serialize(new { SomeProperty = 1 }, MediaEngineJson.Web);

        Assert.Contains("\"someProperty\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Web_IsCaseInsensitiveOnRead()
    {
        var value = JsonSerializer.Deserialize<SamplePayload>("{\"SOMEVALUE\":\"x\"}", MediaEngineJson.Web);

        Assert.Equal("x", value?.SomeValue);
    }

    [Fact]
    public void Indented_ProducesMultilineOutput()
    {
        var json = JsonSerializer.Serialize(new { A = 1 }, MediaEngineJson.Indented);

        Assert.Contains('\n', json);
    }

    [Fact]
    public void CaseInsensitive_ReadsPropertiesRegardlessOfCasing()
    {
        var value = JsonSerializer.Deserialize<SamplePayload>("{\"someValue\":\"y\"}", MediaEngineJson.CaseInsensitive);

        Assert.Equal("y", value?.SomeValue);
    }

    [Fact]
    public void Instances_AreCachedSingletonsAcrossCalls()
    {
        Assert.Same(MediaEngineJson.Web, MediaEngineJson.Web);
        Assert.Same(MediaEngineJson.Indented, MediaEngineJson.Indented);
        Assert.Same(MediaEngineJson.CaseInsensitive, MediaEngineJson.CaseInsensitive);
    }

    private sealed class SamplePayload
    {
        public string? SomeValue { get; set; }
    }
}
