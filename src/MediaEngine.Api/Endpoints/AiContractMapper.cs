using System.Text.Json;
using MediaEngine.AI.Configuration;
using MediaEngine.AI.Infrastructure;
using MediaEngine.Contracts.Ai;

namespace MediaEngine.Api.Endpoints;

internal static class AiContractMapper
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    internal static AiConfigDto ToContract(AiSettings settings)
    {
        var contract = JsonSerializer.Deserialize<AiConfigDto>(
            JsonSerializer.Serialize(settings, JsonOptions),
            JsonOptions)
            ?? throw new InvalidOperationException("The AI configuration could not be mapped to its wire contract.");
        contract.EffectiveResourceProfile = settings.EffectiveResourceProfile;
        return contract;
    }

    internal static AiSettings ToSettings(AiConfigDto contract) =>
        JsonSerializer.Deserialize<AiSettings>(
            JsonSerializer.Serialize(contract, JsonOptions),
            JsonOptions)
        ?? throw new InvalidOperationException("The AI wire contract could not be mapped to configuration.");

    internal static HardwareProfileDto ToContract(HardwareProfile profile) => new()
    {
        Outcome = profile.Outcome,
        Tier = profile.Tier,
        Backend = profile.Backend,
        GpuName = profile.GpuName,
        TokensPerSecond = profile.TokensPerSecond,
        AvailableRamMb = profile.AvailableRamMb,
        BenchmarkedAt = profile.BenchmarkedAt,
        MachineFingerprint = profile.MachineFingerprint,
        BenchmarkModel = profile.BenchmarkModel,
        FailureCode = profile.FailureCode,
        FailureMessage = profile.FailureMessage,
        AdvancedEligible = profile.AdvancedEligible,
    };

}
