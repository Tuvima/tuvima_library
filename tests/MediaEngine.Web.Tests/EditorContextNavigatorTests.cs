using Bunit;
using MediaEngine.Web.Components.MediaEditor;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace MediaEngine.Web.Tests;

public sealed class EditorContextNavigatorTests : AsyncBunitContext
{
    public EditorContextNavigatorTests()
    {
        Services.AddMudServices();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void RendersPersistentHierarchyAndKeepsLevelBodiesActionable()
    {
        var seriesId = Guid.NewGuid();
        var seasonId = Guid.NewGuid();
        var episodeId = Guid.NewGuid();
        var selectedId = Guid.Empty;
        var levels = new List<EditorContextLevel>
        {
            new("Series", "series", "Breaking Bad", null, seriesId, false, true, false, []),
            new("Season", "season", "Season 1", "2 episodes", seasonId, false, true, true,
            [
                new EditorContextOption(seasonId, "Season", "Season 1", "2 episodes", true, true),
            ]),
            new("Episode", "episode", "Cat's in the Bag...", "S1 E2", episodeId, true, true, true,
            [
                new EditorContextOption(episodeId, "Episode", "Cat's in the Bag...", "S1 E2", true, true),
            ]),
        };

        var cut = Render<EditorContextNavigator>(parameters => parameters
            .Add(component => component.Levels, levels)
            .Add(component => component.OnTargetSelected,
                EventCallback.Factory.Create<Guid>(this, id => selectedId = id)));

        Assert.Equal(3, cut.FindAll(".editor-context-level").Count);
        Assert.Equal(2, cut.FindAll(".editor-context__separator").Count);
        Assert.Equal(2, cut.FindAll(".editor-context-level__selector-trigger").Count);
        Assert.Equal("page", cut.Find(".editor-context-level.is-active .editor-context-level__body").GetAttribute("aria-current"));
        Assert.Contains("Series", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Breaking Bad", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Season 1", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Cat's in the Bag...", cut.Markup, StringComparison.Ordinal);

        cut.FindAll(".editor-context-level__body")[0].Click();

        Assert.Equal(seriesId, selectedId);
    }
}
