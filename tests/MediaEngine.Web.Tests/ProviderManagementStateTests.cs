using MediaEngine.Web.Components.Settings;

namespace MediaEngine.Web.Tests;

public sealed class ProviderManagementStateTests
{
    [Fact]
    public void EditorDraft_DoesNotSendAStoredCredentialPlaceholder()
    {
        var item = new ProviderManagementItem
        {
            Key = "tmdb",
            HasKey = true,
            RequiresKey = true,
            Endpoints = new Dictionary<string, string> { ["api"] = "https://example.test" },
        };

        var update = ProviderEditorDraft.From(item).ToUpdate("api");

        Assert.Null(update.ApiKey);
    }

    [Fact]
    public void EditorDraft_SendsOnlyAnExplicitCredentialReplacement()
    {
        var draft = ProviderEditorDraft.From(new ProviderManagementItem { Key = "tmdb" });
        draft.ApiKeyReplacement = "replacement";

        var update = draft.ToUpdate("api");

        Assert.Equal("replacement", update.ApiKey);
    }

    [Fact]
    public void EditorDraft_FingerprintTracksAdvancedAndConnectionChanges()
    {
        var draft = ProviderEditorDraft.From(new ProviderManagementItem { Key = "tmdb" });
        var baseline = draft.Fingerprint();

        draft.MaxConcurrency++;

        Assert.NotEqual(baseline, draft.Fingerprint());
    }
}
