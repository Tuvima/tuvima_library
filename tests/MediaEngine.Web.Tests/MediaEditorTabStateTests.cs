using MediaEngine.Web.Components.MediaEditor;

namespace MediaEngine.Web.Tests;

public sealed class MediaEditorTabStateTests
{
    [Theory]
    [InlineData("identity", "links")]
    [InlineData("universe", "links")]
    [InlineData("inspector", "file")]
    [InlineData(null, "details")]
    public void Initialize_NormalizesEditorEntryTabs(string? requested, string expected)
    {
        var state = new MediaEditorTabState();

        state.Initialize(requested);

        Assert.Equal(expected, state.ActiveTab);
    }

    [Fact]
    public void FileRoundTrip_RetainsLastContentTab()
    {
        var state = new MediaEditorTabState();
        state.Activate("artwork");

        state.ActivateFile();
        state.Activate(state.LastNonFileTab);

        Assert.Equal("artwork", state.ActiveTab);
    }

    [Fact]
    public void EnsureVisible_FallsBackToFirstAvailableTab()
    {
        var state = new MediaEditorTabState();
        state.Activate("history");

        state.EnsureVisible(tab => tab is "details" or "options", ["details", "options"]);

        Assert.Equal("details", state.ActiveTab);
    }
}
