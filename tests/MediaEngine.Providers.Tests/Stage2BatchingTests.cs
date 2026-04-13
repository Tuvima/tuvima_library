using System.Net;
using System.Text;
using Tuvima.Wikidata;

namespace MediaEngine.Providers.Tests;

public sealed class Stage2BatchingTests
{
    [Fact]
    public async Task Stage2ResolveBatchAsync_DeduplicatesOutboundCallsByNaturalKey()
    {
        var handler = new RecordingStage2Handler();
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

        var results = await reconciler.Stage2.ResolveBatchAsync(
        [
            Stage2Request.Bridge(
                correlationKey: "bridge-1",
                bridgeIds: new Dictionary<string, string> { ["imdb"] = "tt0903747" },
                wikidataProperties: new Dictionary<string, string> { ["imdb"] = "P345" }),
            Stage2Request.Bridge(
                correlationKey: "bridge-2",
                bridgeIds: new Dictionary<string, string> { ["imdb"] = "tt0903747" },
                wikidataProperties: new Dictionary<string, string> { ["imdb"] = "P345" }),
            Stage2Request.Music("music-1", "A Night at the Opera", "Queen"),
            Stage2Request.Music("music-2", "A Night at the Opera", "Queen"),
            Stage2Request.Music("music-3", "News of the World", "Queen"),
            Stage2Request.Text(
                correlationKey: "text-1",
                title: "Batman",
                cirrusSearchTypes: ["Q1004"]),
            Stage2Request.Text(
                correlationKey: "text-2",
                title: "Batman",
                cirrusSearchTypes: ["Q1004"]),
            Stage2Request.Text(
                correlationKey: "text-3",
                title: "Saga",
                cirrusSearchTypes: ["Q1004"]),
        ]);

        Assert.Equal(8, results.Count);
        Assert.All(results.Values, result => Assert.False(result.Found));

        Assert.Equal(1, handler.CountQuerySearch("haswbstatement:P345=tt0903747"));

        Assert.Equal(1, handler.CountWbSearch("A Night at the Opera"));
        Assert.Equal(1, handler.CountFullTextSearch("A Night at the Opera"));
        Assert.Equal(1, handler.CountQuerySearch("A Night at the Opera (haswbstatement:P31=Q482994)"));

        Assert.Equal(1, handler.CountWbSearch("News of the World"));
        Assert.Equal(1, handler.CountFullTextSearch("News of the World"));
        Assert.Equal(1, handler.CountQuerySearch("News of the World (haswbstatement:P31=Q482994)"));

        Assert.Equal(1, handler.CountWbSearch("Batman"));
        Assert.Equal(1, handler.CountFullTextSearch("Batman"));
        Assert.Equal(1, handler.CountQuerySearch("Batman (haswbstatement:P31=Q1004)"));

        Assert.Equal(1, handler.CountWbSearch("Saga"));
        Assert.Equal(1, handler.CountFullTextSearch("Saga"));
        Assert.Equal(1, handler.CountQuerySearch("Saga (haswbstatement:P31=Q1004)"));
    }

    private sealed class RecordingStage2Handler : HttpMessageHandler
    {
        private readonly List<RecordedRequest> _requests = [];

        public int CountWbSearch(string search) =>
            _requests.Count(r =>
                string.Equals(r.Action, "wbsearchentities", StringComparison.OrdinalIgnoreCase)
                && string.Equals(r.Search, search, StringComparison.Ordinal));

        public int CountFullTextSearch(string search) =>
            _requests.Count(r =>
                string.Equals(r.Action, "query", StringComparison.OrdinalIgnoreCase)
                && string.Equals(r.List, "search", StringComparison.OrdinalIgnoreCase)
                && string.Equals(r.SrWhat, "text", StringComparison.OrdinalIgnoreCase)
                && string.Equals(r.SrSearch, search, StringComparison.Ordinal));

        public int CountQuerySearch(string search) =>
            _requests.Count(r =>
                string.Equals(r.Action, "query", StringComparison.OrdinalIgnoreCase)
                && string.Equals(r.List, "search", StringComparison.OrdinalIgnoreCase)
                && string.Equals(r.SrSearch, search, StringComparison.Ordinal));

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var recorded = RecordedRequest.From(request.RequestUri);
            _requests.Add(recorded);

            string body = recorded.Action switch
            {
                "wbsearchentities" => """{"search":[]}""",
                "query" when string.Equals(recorded.List, "search", StringComparison.OrdinalIgnoreCase)
                    => """{"query":{"search":[]}}""",
                _ => """{}""",
            };

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed record RecordedRequest(
        string? Action,
        string? List,
        string? Search,
        string? SrSearch,
        string? SrWhat)
    {
        public static RecordedRequest From(Uri? uri)
        {
            var query = ParseQuery(uri?.Query);
            query.TryGetValue("action", out var action);
            query.TryGetValue("list", out var list);
            query.TryGetValue("search", out var search);
            query.TryGetValue("srsearch", out var srsearch);
            query.TryGetValue("srwhat", out var srwhat);
            return new RecordedRequest(action, list, search, srsearch, srwhat);
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
