using System.Text.Json;
using MediaEngine.AI.Configuration;
using MediaEngine.AI.Infrastructure;
using MediaEngine.Domain.Enums;

namespace MediaEngine.AI.Tests;

public sealed class Phase7AiConfigurationTests
{
    [Fact]
    public void ConfigModel_RoundTripsFeatureVocabularyScheduleAndAllModelRoles()
    {
        var settings = new AiSettings();
        var json = JsonSerializer.Serialize(settings);
        var roundTrip = JsonSerializer.Deserialize<AiSettings>(json);

        Assert.NotNull(roundTrip);
        Assert.NotNull(roundTrip.Models.GetByRole(AiModelRole.TextFast));
        Assert.NotNull(roundTrip.Models.GetByRole(AiModelRole.TextQuality));
        Assert.NotNull(roundTrip.Models.GetByRole(AiModelRole.TextScholar));
        Assert.NotNull(roundTrip.Models.GetByRole(AiModelRole.Audio));
        Assert.NotNull(roundTrip.Models.GetByRole(AiModelRole.TextCjk));
        Assert.True(roundTrip.Features.IntentSearch);
        Assert.NotEmpty(roundTrip.VibeVocabulary.Books);
        Assert.False(string.IsNullOrWhiteSpace(roundTrip.Scheduling.VibeBatchCron));
        Assert.False(string.IsNullOrWhiteSpace(roundTrip.Scheduling.DescriptionIntelligence));
    }

    [Fact]
    public void DefaultConfig_UsesSmallFirstModelCatalogAndRoleRequirements()
    {
        var settings = new AiSettings();

        Assert.Equal("qwen3_0_6b_q8", settings.Models.TextFast.CatalogKey);
        Assert.Equal("qwen3_1_7b_q8", settings.Models.TextQuality.CatalogKey);
        Assert.Equal("qwen3_4b_q4", settings.Models.TextScholar.CatalogKey);
        Assert.Equal("whisper_medium", settings.Models.Audio.CatalogKey);
        Assert.True(settings.Models.TextScholar.SizeMB < 3000);

        Assert.Contains("gemma4_12b_it", settings.ModelCatalog.Keys);
        Assert.Equal("escalation", settings.ModelCatalog["gemma4_12b_it"].Status);
        Assert.DoesNotContain("text_fast", settings.ModelCatalog["gemma4_12b_it"].IntendedRoles);
        Assert.True(settings.ModelCatalog["whisper_medium"].Capabilities.SyncGrade);
        Assert.False(settings.ModelCatalog["gemma4_e2b_it"].Capabilities.SyncGrade);

        var audioRequirements = settings.RoleRequirements["audio"];
        Assert.Contains("timestamp_segments", audioRequirements.RequiredCapabilities);
        Assert.Equal("audio_sync", audioRequirements.BenchmarkSuite);
    }

    [Fact]
    public void SelectionAdvisor_FlagsEscalationAndMissingSyncCapabilities()
    {
        var settings = new AiSettings();
        settings.Models.TextScholar.CatalogKey = "gemma4_12b_it";
        settings.Models.TextScholar.File = settings.ModelCatalog["gemma4_12b_it"].File;
        settings.Models.TextScholar.SizeMB = settings.ModelCatalog["gemma4_12b_it"].SizeMB;
        settings.Models.Audio.CatalogKey = "gemma4_e2b_it";
        settings.Models.Audio.File = settings.ModelCatalog["gemma4_e2b_it"].File;
        settings.Models.Audio.SizeMB = settings.ModelCatalog["gemma4_e2b_it"].SizeMB;

        var advisor = new AiModelSelectionAdvisor(settings);

        var scholar = advisor.GetDecision(AiModelRole.TextScholar);
        Assert.Contains(scholar.Warnings, warning => warning.Contains("Escalation model", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(scholar.Warnings, warning => warning.Contains("above the role default cap", StringComparison.OrdinalIgnoreCase));

        var audio = advisor.GetDecision(AiModelRole.Audio);
        Assert.Contains(audio.Warnings, warning => warning.Contains("timestamp_segments", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(audio.Warnings, warning => warning.Contains("sync_grade", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BenchmarkHarness_ProvidesRequiredSuitesAndJsonEvaluation()
    {
        var harness = new AiBenchmarkHarness();
        var suites = harness.GetBuiltInSuites();

        Assert.Contains(suites, suite => suite.Key == "text_instant" && suite.Role == AiModelRole.TextFast);
        Assert.Contains(suites, suite => suite.Key == "text_enrichment" && suite.Role == AiModelRole.TextScholar);
        Assert.Contains(suites, suite => suite.Key == "audio_sync" && suite.Gates.MaxTimestampDriftMs == 250);

        Assert.True(harness.EvaluateJsonOutput("""{"title":"Dune"}""").Passed);
        Assert.True(harness.EvaluateJsonOutput("""prefix {"title":"Dune"} suffix""").Passed);
        Assert.False(harness.EvaluateJsonOutput("not json").Passed);
    }
}
