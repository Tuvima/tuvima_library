using System.Net;
using System.Security.Cryptography;
using System.Text;
using MediaEngine.Api.Services.Webhooks;

namespace MediaEngine.Api.Tests;

public sealed class WebhookTransportTests
{
    [Theory]
    [InlineData("127.0.0.1", true, false)]
    [InlineData("169.254.169.254", true, false)]
    [InlineData("::1", true, false)]
    [InlineData("::ffff:127.0.0.1", true, false)]
    [InlineData("fe80::1", true, false)]
    [InlineData("0.0.0.0", true, false)]
    [InlineData("224.0.0.1", true, false)]
    [InlineData("10.0.1.20", false, false)]
    [InlineData("10.0.1.20", true, true)]
    [InlineData("172.16.0.2", true, true)]
    [InlineData("192.168.1.2", true, true)]
    [InlineData("fd00::10", true, true)]
    [InlineData("fd00::10", false, false)]
    [InlineData("8.8.8.8", false, true)]
    [InlineData("2606:4700:4700::1111", false, true)]
    public void DestinationRequiresExplicitLocalApprovalAndNeverAllowsHostMetadata(string ip, bool allowLocal, bool expected) =>
        Assert.Equal(expected, WebhookDestinationPolicy.IsAllowed(IPAddress.Parse(ip), allowLocal));

    [Theory]
    [InlineData("file:///tmp/receiver")]
    [InlineData("https://user:secret@example.invalid/")]
    [InlineData("https://example.invalid/#fragment")]
    [InlineData("http://example.invalid/")]
    public void InvalidDestinationFailsBeforeDns(string url) =>
        Assert.Throws<WebhookDestinationException>(() => WebhookDestinationPolicy.Parse(url, false));

    [Fact]
    public async Task EveryConnectionRechecksDns_AndMixedAnswersFailClosed()
    {
        var dns = new StubDns([IPAddress.Parse("8.8.8.8")]);
        var policy = new WebhookDestinationPolicy(dns);
        var uri = WebhookDestinationPolicy.Parse("https://receiver.example.invalid/events", false);
        Assert.Single(await policy.ResolveApprovedAsync(uri, false, default));
        dns.Addresses = [IPAddress.Parse("8.8.8.8"), IPAddress.Parse("127.0.0.1")];
        await Assert.ThrowsAsync<WebhookDestinationException>(() => policy.ResolveApprovedAsync(uri, false, default));
        Assert.Equal(2, dns.Calls);
        using var handler = policy.CreateHandler(uri, false);
        Assert.False(handler.AllowAutoRedirect);
        Assert.False(handler.UseProxy);
        Assert.NotNull(handler.ConnectCallback);
    }

    [Fact]
    public async Task PlainHttpRequiresApprovedPrivateReceiver()
    {
        var dns = new StubDns([IPAddress.Parse("10.0.0.42")]);
        var policy = new WebhookDestinationPolicy(dns);
        var uri = WebhookDestinationPolicy.Parse("http://receiver.example.invalid/events", true);
        Assert.Single(await policy.ResolveApprovedAsync(uri, true, default));
        dns.Addresses = [IPAddress.Parse("8.8.8.8")];
        await Assert.ThrowsAsync<WebhookDestinationException>(() => policy.ResolveApprovedAsync(uri, true, default));
    }

    [Fact]
    public void SignatureCoversTimestampAndExactUtf8Body_AndChangesAfterRotation()
    {
        var body = Encoding.UTF8.GetBytes("{\"title\":\"é\", \"value\":1}");
        var combined = Encoding.UTF8.GetBytes("123.").Concat(body).ToArray();
        var expected = "sha256=" + Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes("test-secret"), combined));
        Assert.Equal(expected, WebhookSignature.Sign("test-secret", 123, body));
        Assert.NotEqual(expected, WebhookSignature.Sign("rotated-secret", 123, body));
        Assert.NotEqual(expected, WebhookSignature.Sign("test-secret", 124, body));
        Assert.NotEqual(expected, WebhookSignature.Sign("test-secret", 123, Encoding.UTF8.GetBytes("{\"title\":\"é\",\"value\":1}")));
    }

    private sealed class StubDns(IPAddress[] addresses) : IWebhookDnsResolver
    {
        public IPAddress[] Addresses { get; set; } = addresses;
        public int Calls { get; private set; }
        public Task<IPAddress[]> ResolveAsync(string host, CancellationToken ct) { Calls++; return Task.FromResult(Addresses); }
    }

    [Theory]
    [InlineData(204, true, false)]
    [InlineData(302, false, false)]
    [InlineData(400, false, false)]
    [InlineData(408, false, true)]
    [InlineData(429, false, true)]
    [InlineData(503, false, true)]
    public async Task ReceiverGetsExactSignedBodyAndStableIdentity_AndStatusControlsRetry(int status, bool delivered, bool retryable)
    {
        var body = Encoding.UTF8.GetBytes("{\"title\":\"é\", \"value\":1}");
        var deliveryId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        using var receiver = new Receiver((HttpStatusCode)status, body, deliveryId, eventId);
        using var client = new HttpClient(receiver);
        var result = await WebhookTransport.SendRequestAsync(client, new("https://receiver.example.invalid/events"),
            "test-secret", deliveryId, eventId, body, 123, default);
        Assert.Equal(delivered, result.Delivered);
        Assert.Equal(retryable, result.Retryable);
        Assert.Equal($"HTTP {status}", result.Status);
        Assert.Equal(1, receiver.Calls);
    }

    private sealed class Receiver(HttpStatusCode status, byte[] expectedBody, Guid deliveryId, Guid eventId) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            Assert.Equal(HttpMethod.Post, request.Method);
            var actual = await request.Content!.ReadAsByteArrayAsync(ct);
            Assert.Equal(expectedBody, actual);
            Assert.Equal("application/json", request.Content.Headers.ContentType!.MediaType);
            Assert.Equal("123", Assert.Single(request.Headers.GetValues("Tuvima-Timestamp")));
            Assert.Equal(deliveryId.ToString("D"), Assert.Single(request.Headers.GetValues("Tuvima-Delivery-Id")));
            Assert.Equal(eventId.ToString("D"), Assert.Single(request.Headers.GetValues("Tuvima-Event-Id")));
            Assert.Equal(WebhookSignature.Sign("test-secret", 123, actual), Assert.Single(request.Headers.GetValues("Tuvima-Signature")));
            var response = new HttpResponseMessage(status);
            response.Headers.Location = new("http://169.254.169.254/blocked");
            return response;
        }
    }
}
