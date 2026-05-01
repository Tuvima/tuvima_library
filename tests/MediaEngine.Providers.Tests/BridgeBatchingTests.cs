using System.Net;
using System.Text;
using Tuvima.Wikidata;

namespace MediaEngine.Providers.Tests;

public sealed class BridgeBatchingTests
{
    [Fact]
    public async Task BridgeResolveBatchAsync_DeduplicatesOutboundBridgeLookups()
    {
        var handler = new RecordingBridgeHandler();
        using var httpClient = new HttpClient(handler);
        using var reconciler = new WikidataReconciler(httpClient, new WikidataReconcilerOptions
        {
            ApiEndpoint = "https://wikidata.test/w/api.php",
            UserAgent = "Tuvima Library Tests",
            MaxLag = 0,
            MaxRetries = 0,
            TypeHierarchyDepth = 0,
            IncludeSitelinkLabels = false,
        });

        var results = await reconciler.Bridge.ResolveBatchAsync(
        [
            new BridgeResolutionRequest
            {
                CorrelationKey = "bridge-1",
                MediaKind = BridgeMediaKind.TvSeries,
                BridgeIds = new Dictionary<string, string> { ["imdb_id"] = "tt0903747" }
            },
            new BridgeResolutionRequest
            {
                CorrelationKey = "bridge-2",
                MediaKind = BridgeMediaKind.TvSeries,
                BridgeIds = new Dictionary<string, string> { ["imdb_id"] = "tt0903747" }
            }
        ]);

        Assert.Equal(2, results.Count);
        Assert.All(results.Values, result => Assert.False(result.Found));
        Assert.Equal(1, handler.CountQuerySearch("haswbstatement:P345=tt0903747"));
    }

    private sealed class RecordingBridgeHandler : HttpMessageHandler
    {
        private readonly List<RecordedRequest> _requests = [];

        public int CountQuerySearch(string search) =>
            _requests.Count(r =>
                string.Equals(r.Action, "query", StringComparison.OrdinalIgnoreCase)
                && string.Equals(r.List, "search", StringComparison.OrdinalIgnoreCase)
                && string.Equals(r.SrSearch, search, StringComparison.Ordinal));

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _requests.Add(RecordedRequest.From(request.RequestUri));

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"query":{"search":[]}}""", Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed record RecordedRequest(
        string? Action,
        string? List,
        string? SrSearch)
    {
        public static RecordedRequest From(Uri? uri)
        {
            var query = ParseQuery(uri?.Query);
            query.TryGetValue("action", out var action);
            query.TryGetValue("list", out var list);
            query.TryGetValue("srsearch", out var srsearch);
            return new RecordedRequest(action, list, srsearch);
        }

        private static Dictionary<string, string> ParseQuery(string? query)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(query))
                return result;

            foreach (var segment in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = segment.Split('=', 2);
                var key = Uri.UnescapeDataString(parts[0]);
                var value = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : string.Empty;
                result[key] = value;
            }

            return result;
        }
    }
}
