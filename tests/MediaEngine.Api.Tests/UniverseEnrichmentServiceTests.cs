using System.Reflection;
using MediaEngine.Api.Services;

namespace MediaEngine.Api.Tests;

public sealed class UniverseEnrichmentServiceTests
{
    [Fact]
    public void HasUniversePath_IgnoresLabelOnlySignals()
    {
        var method = typeof(UniverseEnrichmentService).GetMethod(
            "HasUniversePath",
            BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(method);

        var labelOnly = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["characters"] = "Walter White",
            ["organization"] = "The Syndicate",
        };

        var result = method!.Invoke(null, [labelOnly, null]);

        Assert.NotNull(result);
        Assert.False((bool)result!);
    }

    [Fact]
    public void HasUniversePath_AcceptsSeedQids()
    {
        var method = typeof(UniverseEnrichmentService).GetMethod(
            "HasUniversePath",
            BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(method);

        var actionableSignals = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["characters_qid"] = "Q188987",
        };

        var result = method!.Invoke(null, [actionableSignals, null]);

        Assert.NotNull(result);
        Assert.True((bool)result!);
    }
}
