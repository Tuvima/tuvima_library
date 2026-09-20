using System.Reflection;
using MediaEngine.Contracts.Metadata;
using MediaEngine.Contracts.Matching;
using MediaEngine.Web.Components.MediaEditor;

namespace MediaEngine.Web.Tests;

public sealed class EditorMatchTargetTests
{
    [Theory]
    [InlineData("TV", "series", false)]
    [InlineData("TV", "season", false)]
    [InlineData("TV", "episode", true)]
    [InlineData("Movies", "item", true)]
    [InlineData("Books", "book", true)]
    [InlineData("Music", "album", true)]
    public void MatchingFollowsEditingTargetEvenWhenStaleContextAdvertisesLinks(string mediaType, string scopeId, bool expected)
    {
        var shell = new TargetShell();
        shell.Configure(mediaType, scopeId, "Selected item");
        Assert.Equal(expected, shell.MatchingAllowed);
        Assert.Equal(expected, shell.VisibleTabs.Contains("links"));
        Assert.Equal(expected, shell.SearchAllowed);
        Assert.Equal(expected, shell.CanApplyRetail());
    }

    [Fact]
    public void TargetHeadingDoesNotFollowTheMatchSearchTarget()
    {
        var shell = new TargetShell();
        shell.Configure("TV", "episode", "Pilot");
        shell.SetField("_canonicalTargetGroup", "show");
        Assert.Equal("Pilot", shell.TargetTitle);
    }

    private sealed class TargetShell : SharedMediaEditorShell
    {
        public bool MatchingAllowed => CanMatchCurrentTarget;
        public bool SearchAllowed => SupportsCanonicalSearch;
        public string TargetTitle => CurrentTargetTitle;
        public IEnumerable<string> VisibleTabs => Tabs.Select(tab => tab.Id);
        public bool CanApplyRetail()
        {
            var candidate = new ItemCanonicalRetailCandidateDto { CandidateId = "test", ProviderName = "tmdb", ProviderItemId = "123", IsApplicable = true };
            SetField("_selectedRetailCandidateId", "retail:test:tmdb:123:");
            return CanApplyRetailCandidate(candidate);
        }
        public void Configure(string mediaType, string scopeId, string title)
        {
            SetField("_editorContext", new MediaEditorContextDto
            {
                MediaType = mediaType,
                Scopes = [
                    new() { ScopeId = scopeId, DisplayTitle = title, AvailableTabs = ["details", "artwork", "links"] },
                    new() { ScopeId = "series", DisplayTitle = "Parent series" }
                ]
            });
            SetField("_activeScopeId", scopeId);
        }
        public void SetField(string name, object value) =>
            typeof(SharedMediaEditorShell).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(this, value);
    }
}
