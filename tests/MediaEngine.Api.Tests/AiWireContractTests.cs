using System.Text.Json;
using System.Text.Json.Nodes;
using MediaEngine.AI.Configuration;
using MediaEngine.Api.Endpoints;
using MediaEngine.Contracts.Ai;

namespace MediaEngine.Api.Tests;

public sealed class AiWireContractTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void AiConfigContract_RoundTripsResourceLimitsAndNestedCatalogFields()
    {
        var settings = new AiSettings
        {
            MaxConcurrentInferences = 3,
            MinimumFreeDiskMB = 4096,
        };
        var catalog = settings.ModelCatalog.Values.First();
        catalog.IntendedRoles = ["text_fast"];
        catalog.ParametersB = 1.7;
        catalog.Compatibility.SupportedBackends = ["cpu", "cuda"];
        catalog.Capabilities.StructuredJson = true;
        catalog.Validation.MinTaskPassRate = 0.95;
        settings.OperationalRoles["test_role"] = new AiOperationalRoleDefinition
        {
            CatalogKey = settings.ModelCatalog.Keys.First(),
            MaxOutputTokens = 512,
            MaxConcurrency = 2,
        };

        var contract = AiContractMapper.ToContract(settings);
        var roundTrip = AiContractMapper.ToSettings(contract);
        var settingsJson = JsonSerializer.SerializeToNode(settings, JsonOptions);
        var contractJson = JsonSerializer.SerializeToNode(contract, JsonOptions);

        Assert.Equal(3, contract.MaxConcurrentInferences);
        Assert.Equal(4096, contract.MinimumFreeDiskMB);
        Assert.Equal(1.7, contract.ModelCatalog.Values.First().ParametersB);
        Assert.Equal(["cpu", "cuda"], contract.ModelCatalog.Values.First().Compatibility.SupportedBackends);
        Assert.True(contract.ModelCatalog.Values.First().Capabilities.StructuredJson);
        Assert.Equal(0.95, contract.ModelCatalog.Values.First().Validation.MinTaskPassRate);
        Assert.Equal(512, contract.OperationalRoles["test_role"].MaxOutputTokens);
        Assert.Equal(2, roundTrip.OperationalRoles["test_role"].MaxConcurrency);
        Assert.Equal(3, roundTrip.MaxConcurrentInferences);
        Assert.Equal(4096, roundTrip.MinimumFreeDiskMB);
        Assert.True(JsonNode.DeepEquals(settingsJson, contractJson));
    }

    [Fact]
    public void AiContracts_PreserveResourceLimitAndFractionalCpuJsonNames()
    {
        var configJson = JsonSerializer.Serialize(new AiConfigDto
        {
            MaxConcurrentInferences = 2,
            MinimumFreeDiskMB = 2048,
        }, JsonOptions);
        var resourceJson = JsonSerializer.Serialize(new ResourceSnapshotDto
        {
            CpuPressure = 0.73,
        }, JsonOptions);

        Assert.Contains("\"max_concurrent_inferences\":2", configJson, StringComparison.Ordinal);
        Assert.Contains("\"minimum_free_disk_mb\":2048", configJson, StringComparison.Ordinal);
        Assert.Contains("\"cpu_pressure\":0.73", resourceJson, StringComparison.Ordinal);
    }
}
