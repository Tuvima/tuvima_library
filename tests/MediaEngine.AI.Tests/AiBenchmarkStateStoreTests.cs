using MediaEngine.AI.Configuration;
using MediaEngine.AI.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaEngine.AI.Tests;

public sealed class AiBenchmarkStateStoreTests
{
    [Fact]
    public void ZeroThroughputSuccess_IsDistinctFromFailure()
    {
        var succeeded = new HardwareProfile
        {
            Outcome = AiBenchmarkOutcomes.Succeeded,
            TokensPerSecond = 0,
            AvailableRamMb = 16_384,
        };
        var failed = new HardwareProfile
        {
            Outcome = AiBenchmarkOutcomes.Failed,
            TokensPerSecond = null,
            AvailableRamMb = 16_384,
        };

        Assert.Equal(0, succeeded.TokensPerSecond);
        Assert.Null(failed.TokensPerSecond);
        Assert.False(succeeded.AdvancedEligible);
        Assert.False(failed.AdvancedEligible);
    }

    [Fact]
    public void StoredBenchmark_FromDifferentMachineFingerprint_IsInvalidated()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"tuvima-ai-state-{Guid.NewGuid():N}");
        try
        {
            var settings = new AiSettings { ModelsDirectory = directory };
            var store = new AiBenchmarkStateStore(settings, NullLogger<AiBenchmarkStateStore>.Instance);
            store.Save(new HardwareProfile
            {
                Outcome = AiBenchmarkOutcomes.Succeeded,
                TokensPerSecond = 40,
                AvailableRamMb = 32_768,
                MachineFingerprint = "another-machine",
                Backend = "cpu",
                BenchmarkVersion = "v2",
            });

            var loaded = store.LoadCurrent("cpu", null);

            Assert.Equal(AiBenchmarkOutcomes.Invalidated, loaded.Outcome);
            Assert.Null(loaded.TokensPerSecond);
            Assert.Equal("machine_changed", loaded.FailureCode);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }
}
