using MediaEngine.Api.Services.Playback;
using MediaEngine.Domain.Configuration;

namespace MediaEngine.Api.Tests;

public sealed class AdaptiveHlsProfileTests
{
    [Fact]
    public void RenditionSelection_NeverUpscalesSource()
    {
        var settings = new AdaptiveHlsSettings();

        var selected = AdaptiveHlsService.SelectRenditions(settings, 720);

        Assert.Equal([720, 480], selected.Select(rendition => rendition.Height));
    }

    [Fact]
    public void RenditionSelection_KeepsSmallSourcePlayable()
    {
        var settings = new AdaptiveHlsSettings();

        var selected = AdaptiveHlsService.SelectRenditions(settings, 360);

        var rendition = Assert.Single(selected);
        Assert.Equal(360, rendition.Height);
    }
}
