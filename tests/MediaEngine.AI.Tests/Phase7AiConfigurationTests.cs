using System.Text.Json;
using MediaEngine.AI.Configuration;
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
}
