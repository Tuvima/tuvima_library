using System.Net;
using System.Text;
using MediaEngine.Api.Services.Collections;
using MediaEngine.Domain;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Providers.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaEngine.Api.Tests;

public sealed class AlbumTrackManifestRepairTests
{
    [Fact]
    public async Task LegacyBoxSetManifest_IsReplacedByResolvedAlbumManifest()
    {
        var rootWorkId = Guid.NewGuid();
        var canonicalRepo = new StubCanonicalValueRepository();
        var client = new AppleRetailClient(
            new RoutingHttpClientFactory(request =>
            {
                var url = request.RequestUri!.ToString();
                if (url.Contains("/search?", StringComparison.OrdinalIgnoreCase))
                {
                    return JsonResponse("""
                        {
                          "results": [
                            { "collectionName": "A New Career in a New Town (1977-1982)", "artistName": "David Bowie", "collectionId": 1255088551 },
                            { "collectionName": "\"Heroes\" (2017 Remaster)", "artistName": "David Bowie", "collectionId": 1347894082 }
                          ]
                        }
                        """);
                }

                return JsonResponse("""
                    {
                      "results": [
                        { "wrapperType": "collection", "collectionId": 1347894082 },
                        { "wrapperType": "track", "kind": "song", "trackName": "Beauty and the Beast", "trackNumber": 1, "discNumber": 1, "trackTimeMillis": 215000, "trackId": 1347894083 },
                        { "wrapperType": "track", "kind": "music-video", "trackName": "Documentary", "trackNumber": 2, "trackTimeMillis": 600000, "trackId": 999 },
                        { "wrapperType": "track", "kind": "song", "trackName": "Heroes", "trackNumber": 3, "discNumber": 1, "trackTimeMillis": 370000, "trackId": 1347894085 }
                      ]
                    }
                    """);
            }),
            new RetailRequestBuilder(),
            new ProviderRateLimiterCoordinator(),
            NullLogger<AppleRetailClient>.Instance);
        var service = new AlbumTrackManifestService(canonicalRepo, client);
        var legacyManifest = """
            {
              "tracks": [
                { "title": "Wrong Box Set Track", "ordinal": 1, "track_number": 1, "duration_seconds": 100, "source": "apple_itunes" }
              ]
            }
            """;

        var result = await service.EnsureAppleAlbumTrackManifestAsync(
            rootWorkId,
            "David Bowie",
            "Heroes",
            legacyManifest,
            [],
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.Contains("\"provider_collection_id\":\"1347894082\"", result, StringComparison.Ordinal);
        Assert.Contains("\"Beauty and the Beast\"", result, StringComparison.Ordinal);
        Assert.Contains("\"Heroes\"", result, StringComparison.Ordinal);
        Assert.DoesNotContain("Wrong Box Set Track", result, StringComparison.Ordinal);
        Assert.DoesNotContain("Documentary", result, StringComparison.Ordinal);
        Assert.Contains(canonicalRepo.Values, value =>
            value.EntityId == rootWorkId
            && value.Key == MetadataFieldConstants.TrackCount
            && value.Value == "2");
        Assert.Contains(canonicalRepo.Values, value =>
            value.EntityId == rootWorkId
            && value.Key == BridgeIdKeys.AppleMusicCollectionId
            && value.Value == "1347894082");
    }

    private static HttpResponseMessage JsonResponse(string json)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

    private sealed class RoutingHttpClientFactory(
        Func<HttpRequestMessage, HttpResponseMessage> responder) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
            => new(new RoutingHttpMessageHandler(responder), disposeHandler: true);
    }

    private sealed class RoutingHttpMessageHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(responder(request));
    }

    private sealed class StubCanonicalValueRepository : ICanonicalValueRepository
    {
        public List<CanonicalValue> Values { get; } = [];

        public Task<IReadOnlyList<CanonicalValue>> GetByEntityAsync(
            Guid entityId,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<CanonicalValue>>(
                Values.Where(value => value.EntityId == entityId).ToList());

        public Task<IReadOnlyDictionary<Guid, IReadOnlyList<CanonicalValue>>> GetByEntitiesAsync(
            IReadOnlyList<Guid> entityIds,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyDictionary<Guid, IReadOnlyList<CanonicalValue>>>(
                entityIds.ToDictionary(
                    entityId => entityId,
                    entityId => (IReadOnlyList<CanonicalValue>)Values
                        .Where(value => value.EntityId == entityId)
                        .ToList()));

        public Task UpsertBatchAsync(
            IReadOnlyList<CanonicalValue> values,
            CancellationToken ct = default)
        {
            foreach (var value in values)
            {
                Values.RemoveAll(existing =>
                    existing.EntityId == value.EntityId
                    && string.Equals(existing.Key, value.Key, StringComparison.OrdinalIgnoreCase));
                Values.Add(value);
            }

            return Task.CompletedTask;
        }

        public Task DeleteByEntityAsync(Guid entityId, CancellationToken ct = default)
        {
            Values.RemoveAll(value => value.EntityId == entityId);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<CanonicalValue>> GetConflictedAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<CanonicalValue>>([]);

        public Task DeleteByKeyAsync(Guid entityId, string key, CancellationToken ct = default)
        {
            Values.RemoveAll(value =>
                value.EntityId == entityId
                && string.Equals(value.Key, key, StringComparison.OrdinalIgnoreCase));
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<Guid>> FindByValueAsync(
            string key,
            string value,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Guid>>([]);

        public Task<IReadOnlyList<CanonicalValue>> FindByKeyAndPrefixAsync(
            string key,
            string prefix,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<CanonicalValue>>([]);

        public Task<IReadOnlyList<Guid>> GetEntitiesNeedingEnrichmentAsync(
            string hasField,
            string missingField,
            int limit,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Guid>>([]);
    }
}
