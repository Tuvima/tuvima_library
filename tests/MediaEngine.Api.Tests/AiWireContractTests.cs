using System.Text.Json;
using MediaEngine.AI.Configuration;
using MediaEngine.Api.Endpoints;
using MediaEngine.Contracts.Ai;

namespace MediaEngine.Api.Tests;

public sealed class AiWireContractTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void AiConfigContract_RoundTripsProfilesWithoutRawCatalogOrHardwareState()
    {
        var settings = new AiSettings
        {
            ResourceProfile = AiResourceProfileNames.Essential,
            AudioPackEnabled = true,
            MinimumFreeDiskMB = 4096,
        };

        var contract = AiContractMapper.ToContract(settings);
        var roundTrip = AiContractMapper.ToSettings(contract);
        var json = JsonSerializer.Serialize(contract, JsonOptions);

        Assert.Equal(AiResourceProfileNames.Essential, roundTrip.ResourceProfile);
        Assert.Equal(AiResourceProfileNames.Essential, contract.EffectiveResourceProfile);
        Assert.True(roundTrip.AudioPackEnabled);
        Assert.Equal(4096, roundTrip.MinimumFreeDiskMB);
        Assert.DoesNotContain("model_catalog", json, StringComparison.Ordinal);
        Assert.DoesNotContain("operational_roles", json, StringComparison.Ordinal);
        Assert.DoesNotContain("hardware_profile", json, StringComparison.Ordinal);
    }

    [Fact]
    public void HardwareContract_DistinguishesFailureFromZeroThroughputSuccess()
    {
        var failed = AiContractMapper.ToContract(new HardwareProfile
        {
            Outcome = AiBenchmarkOutcomes.Failed,
            TokensPerSecond = null,
        });
        var zero = AiContractMapper.ToContract(new HardwareProfile
        {
            Outcome = AiBenchmarkOutcomes.Succeeded,
            TokensPerSecond = 0,
        });

        Assert.Null(failed.TokensPerSecond);
        Assert.Equal(0, zero.TokensPerSecond);
    }
}
