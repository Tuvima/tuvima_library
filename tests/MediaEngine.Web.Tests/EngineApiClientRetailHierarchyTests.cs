using System.Net;
using System.Text;
using System.Text.Json;
using MediaEngine.Contracts.Matching;
using MediaEngine.Contracts.Metadata;
using MediaEngine.Web.Services.Integration;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaEngine.Web.Tests;

public sealed class EngineApiClientRetailHierarchyTests
{
    [Fact]
    public async Task PreviewAndApplyKeepTypedHierarchyPathsAndSelectedEntity()
    {
        var selectedEntityId = Guid.NewGuid();
        var targetRootEntityId = Guid.NewGuid();
        var targetParentEntityId = Guid.NewGuid();
        var handler = new CapturingResponseHandler(
            $$"""{"action":"move_child","current_path":"Old Show / Season 1 / Pilot","target_path":"New Show / Season 2 / Pilot","can_apply":true,"selected_entity_id":"{{selectedEntityId}}"}""",
            $$"""{"hierarchy_changed":false,"selected_entity_id":"{{selectedEntityId}}","target_root_entity_id":"{{targetRootEntityId}}","target_parent_entity_id":"{{targetParentEntityId}}","previous_path":"Album / Disc 1 / Track 1","target_path":"Album / Disc 1 / Track 1"}""");
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://engine.test") };
        using var api = new EngineApiClient(http, NullLogger<EngineApiClient>.Instance);

        var preview = await api.PreviewMediaEditorMembershipAsync(selectedEntityId, new MediaEditorMembershipPreviewRequestDto
        {
            ScopeId = "episode",
            FieldValues = new Dictionary<string, string?> { ["show_name"] = "New Show" },
            SelectedSuggestions = new Dictionary<string, MediaEditorMembershipSuggestionDto>
            {
                ["show"] = new()
                {
                    Source = "retail",
                    Kind = "show",
                    Label = "New Show",
                    ProviderName = "tmdb",
                    ProviderItemId = "42",
                    ExternalIdKey = "tmdb_id",
                    ExternalIdValue = "42",
                },
            },
        });
        var apply = await api.ReplaceRetailMatchAsync(selectedEntityId, new ReplaceRetailMatchRequestDto
        {
            TargetKind = "work",
            TargetFieldGroup = "show_episode",
            TargetScopeId = "episode",
            ProviderId = "tmdb",
            ProviderName = "tmdb",
            ProviderItemId = "episode-42",
            RequiredFields = new Dictionary<string, string> { ["show_name"] = "New Show" },
            SuggestedFields = new Dictionary<string, string> { ["episode_title"] = "Pilot" },
            BridgeIds = new Dictionary<string, string> { ["tmdb_id"] = "42" },
        });

        Assert.NotNull(preview);
        Assert.Equal("move_child", preview.Action);
        Assert.Equal("Old Show / Season 1 / Pilot", preview.CurrentPath);
        Assert.Equal("New Show / Season 2 / Pilot", preview.TargetPath);
        Assert.NotNull(apply);
        Assert.False(apply.HierarchyChanged);
        Assert.Equal(selectedEntityId, apply.SelectedEntityId);
        Assert.Equal(targetRootEntityId, apply.TargetRootEntityId);
        Assert.Equal(targetParentEntityId, apply.TargetParentEntityId);
        Assert.Equal("Album / Disc 1 / Track 1", apply.PreviousPath);
        Assert.Equal("Album / Disc 1 / Track 1", apply.TargetPath);

        Assert.Equal("/metadata/" + selectedEntityId + "/membership-preview", handler.Requests[0].Path);
        Assert.Equal("/library/items/" + selectedEntityId + "/retail-match", handler.Requests[1].Path);
        using var previewRequest = JsonDocument.Parse(handler.Requests[0].Body);
        Assert.Equal("New Show", previewRequest.RootElement.GetProperty("field_values").GetProperty("show_name").GetString());
        Assert.Equal("42", previewRequest.RootElement.GetProperty("selected_suggestions").GetProperty("show").GetProperty("external_id_value").GetString());
        using var applyRequest = JsonDocument.Parse(handler.Requests[1].Body);
        Assert.Equal("New Show", applyRequest.RootElement.GetProperty("required_fields").GetProperty("show_name").GetString());
        Assert.Equal("Pilot", applyRequest.RootElement.GetProperty("suggested_fields").GetProperty("episode_title").GetString());
    }

    private sealed class CapturingResponseHandler(params string[] responses) : HttpMessageHandler
    {
        private readonly Queue<string> _responses = new(responses);
        public List<(string Path, string Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.RequestUri!.PathAndQuery, await request.Content!.ReadAsStringAsync(cancellationToken)));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_responses.Dequeue(), Encoding.UTF8, "application/json"),
            };
        }
    }
}
