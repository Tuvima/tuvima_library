using MediaEngine.Web.Components.Settings;

namespace MediaEngine.Web.Tests;

public sealed class ProviderManagementStateTests
{
    [Fact]
    public void ProviderSettings_UsesCatalogueCredentialFieldsWithoutProviderSpecificInstructions()
    {
        var page = File.ReadAllText(FindRepoFile(
            "src", "MediaEngine.Web", "Components", "Settings", "MetadataSettingsPage.razor"));

        Assert.Contains("Catalogue.Onboarding?.Credentials", page, StringComparison.Ordinal);
        Assert.Contains("credential.Label", page, StringComparison.Ordinal);
        Assert.DoesNotContain("TMDB API Key", page, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Comic Vine API Key", page, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EditorDraft_DoesNotSendCredentialsThroughProviderConfiguration()
    {
        var item = new ProviderManagementItem
        {
            Key = "tmdb",
            HasKey = true,
            RequiresKey = true,
            Endpoints = new Dictionary<string, string> { ["api"] = "https://example.test" },
        };

        var draft = ProviderEditorDraft.From(item);
        var update = draft.ToUpdate("api");

        Assert.NotNull(update);
        Assert.Empty(draft.ToCredentialRequest().Credentials);
    }

    [Fact]
    public void EditorDraft_SendsOnlyAnExplicitCredentialReplacement()
    {
        var draft = ProviderEditorDraft.From(new ProviderManagementItem { Key = "tmdb" });
        draft.CredentialReplacements["api_key"] = "replacement";

        var request = draft.ToCredentialRequest();

        Assert.Equal("replacement", request.Credentials["api_key"]);
        Assert.DoesNotContain("replacement", draft.Fingerprint(), StringComparison.Ordinal);
    }

    [Fact]
    public void EditorDraft_FingerprintTracksAdvancedAndConnectionChanges()
    {
        var draft = ProviderEditorDraft.From(new ProviderManagementItem { Key = "tmdb" });
        var baseline = draft.Fingerprint();

        draft.MaxConcurrency++;

        Assert.NotEqual(baseline, draft.Fingerprint());
    }

    private static string FindRepoFile(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine([directory.FullName, .. segments]);
            if (File.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException(string.Join(Path.DirectorySeparatorChar, segments));
    }
}
