using System.Net;
using System.Text;
using MediaEngine.Providers.Services;
using MediaEngine.Storage.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaEngine.Providers.Tests;

public sealed class CommonsImageResolverTests
{
    [Fact]
    public async Task ResolveAndDownloadPersonImageAsync_DownloadsHeadshotWithEscapedCommonsName()
    {
        var requestedUrls = new List<string>();
        var resolver = new CommonsImageResolver(
            new ReconciliationProviderConfig(),
            new RoutingHttpClientFactory(request =>
            {
                requestedUrls.Add(request.RequestUri!.ToString());
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(Encoding.UTF8.GetBytes("image")),
                };
            }),
            NullLogger<CommonsImageResolver>.Instance);
        var dir = Path.Combine(Path.GetTempPath(), "tuvima-commons-test-" + Guid.NewGuid());

        try
        {
            var path = await resolver.ResolveAndDownloadPersonImageAsync(
                "wikidata_reconciliation",
                "Frank Herbert.jpg",
                dir,
                CancellationToken.None);

            Assert.NotNull(path);
            Assert.True(File.Exists(path));
            Assert.EndsWith("headshot.jpg", path, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Frank_Herbert.jpg", requestedUrls.Single(), StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task ResolveAndDownloadPersonImageAsync_ReturnsNullOnHttpFailureAndPropagatesCancellation()
    {
        var failing = new CommonsImageResolver(
            new ReconciliationProviderConfig(),
            new RoutingHttpClientFactory(_ => new HttpResponseMessage(HttpStatusCode.NotFound)),
            NullLogger<CommonsImageResolver>.Instance);

        var failed = await failing.ResolveAndDownloadPersonImageAsync(
            "wikidata_reconciliation",
            "missing.jpg",
            Path.GetTempPath(),
            CancellationToken.None);

        Assert.Null(failed);

        var cancelling = new CommonsImageResolver(
            new ReconciliationProviderConfig(),
            new RoutingHttpClientFactory(_ => throw new OperationCanceledException()),
            NullLogger<CommonsImageResolver>.Instance);

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            cancelling.ResolveAndDownloadPersonImageAsync(
                "wikidata_reconciliation",
                "cancel.jpg",
                Path.GetTempPath(),
                CancellationToken.None));
    }

    private sealed class RoutingHttpClientFactory : IHttpClientFactory
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public RoutingHttpClientFactory(Func<HttpRequestMessage, HttpResponseMessage> responder)
            => _responder = responder;

        public HttpClient CreateClient(string name)
            => new(new RoutingHttpMessageHandler(_responder), disposeHandler: true);
    }

    private sealed class RoutingHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public RoutingHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
            => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_responder(request));
    }
}
