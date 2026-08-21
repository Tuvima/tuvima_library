using Bunit;
using MediaEngine.Contracts.Details;
using MediaEngine.Web.Components.Details;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace MediaEngine.Web.Tests;

public sealed class DetailPresentationCapabilityTests : AsyncBunitContext
{
    public DetailPresentationCapabilityTests()
    {
        Services.AddMudServices();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Theory]
    [InlineData(DetailEntityType.MusicAlbum, true, false, "tl-detail-stage--music-album")]
    [InlineData(DetailEntityType.Audiobook, false, true, "tl-detail-stage--audiobook")]
    [InlineData(DetailEntityType.Book, false, false, "tl-detail-stage--bookish")]
    [InlineData(DetailEntityType.Movie, false, false, "tl-detail-stage--watch")]
    public void Capabilities_KeepAlbumAndAudiobookPresentationDistinct(
        DetailEntityType entityType,
        bool isMusicAlbum,
        bool isAudiobook,
        string stageModifier)
    {
        var capabilities = DetailPresentationCapabilities.For(entityType);

        Assert.Equal(isMusicAlbum, capabilities.IsMusicAlbum);
        Assert.Equal(isAudiobook, capabilities.IsAudiobook);
        Assert.Equal(stageModifier, capabilities.StageModifier);
        Assert.Equal(isMusicAlbum, capabilities.SupportsCanonicalMissingItems);
    }

    [Fact]
    public void HeroGenreList_ShowsTwoGenresAndKeyboardAccessibleOverflow()
    {
        var genres = new[]
        {
            Genre("Science Fiction"),
            Genre("Neo-Noir"),
            Genre("Thriller"),
            Genre("Mystery"),
        };

        var cut = Render<HeroGenreList>(parameters => parameters
            .Add(component => component.Genres, genres));

        Assert.Equal(2, cut.FindAll(".tl-detail-hero-genre").Count);
        var overflow = cut.Find("summary");
        Assert.Equal("+2", overflow.TextContent.Trim());
        Assert.Equal("Show 2 more genres", overflow.GetAttribute("aria-label"));
        Assert.Equal(2, cut.FindAll("[role='menuitem']").Count);
    }

    [Fact]
    public void HeroProgressBlock_UsesStructuredListeningProgressSemantics()
    {
        var cut = Render<HeroProgressBlock>(parameters => parameters
            .Add(component => component.Progress, new ProgressViewModel
            {
                Kind = DetailProgressKind.Listening,
                Percent = 13,
                Label = "Continue listening",
                ContextLabel = "Track 6 of 50",
                PercentLabel = "13% complete",
                RemainingLabel = "11h 46m remaining",
                SecondaryLabel = "44 tracks remaining",
            }));

        var progressbar = cut.Find("[role='progressbar']");
        Assert.Equal("13", progressbar.GetAttribute("aria-valuenow"));
        Assert.Equal("Continue listening", progressbar.GetAttribute("aria-label"));
        Assert.Contains("Track 6 of 50", cut.Markup);
        Assert.Contains("44 tracks remaining", cut.Markup);
    }

    [Fact]
    public void HeroActionRow_KeepsMusicModeExplicit()
    {
        var cut = Render<HeroActionRow>(parameters => parameters
            .Add(component => component.IsMusicAlbum, true)
            .Add(component => component.PrimaryActions, new[]
            {
                new DetailAction { Key = "play-album", Label = "Play", Icon = "play_arrow", IsPrimary = true },
                new DetailAction { Key = "shuffle", Label = "Shuffle", Icon = "shuffle", IsPrimary = true },
            }));

        Assert.Single(cut.FindAll(".tl-detail-actions--music-album"));
        Assert.Equal(2, cut.FindAll(".tl-detail-action--primary").Count);
        Assert.Contains("Shuffle", cut.Markup);
    }

    private static MetadataPill Genre(string label) => new()
    {
        Label = label,
        Kind = "genre",
        Route = $"/search?genre={Uri.EscapeDataString(label)}",
    };
}
