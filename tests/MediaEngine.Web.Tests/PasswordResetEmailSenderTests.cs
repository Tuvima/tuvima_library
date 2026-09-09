using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using MediaEngine.Contracts.Authentication;
using MediaEngine.Domain.Configuration;
using MediaEngine.Web.Services.Integration;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaEngine.Web.Tests;

public sealed class PasswordResetEmailSenderTests
{
    [Fact]
    public async Task UserTriggeredTestEmail_UsesConfiguredSmtpWithoutExternalDelivery()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var conversation = ReceiveOneMessageAsync(listener, timeout.Token);
        var sender = new PasswordResetEmailSender(new PasswordResetDeliverySettings
        {
            Mode = "Smtp",
            PublicBaseUrl = "http://localhost:5016",
            SmtpHost = "127.0.0.1",
            SmtpPort = port,
            FromAddress = "library@example.test",
            FromName = "Tuvima Library",
            UseStartTls = false,
        }, NullLogger<PasswordResetEmailSender>.Instance);

        var identity = new DashboardIdentityClient(new AccountClientFactory("owner@example.test"));
        var sent = await DashboardAuthenticationEndpoints.SendCurrentAccountTestEmailAsync(
            identity, sender, timeout.Token);
        var transcript = await conversation;

        Assert.True(sent);
        Assert.Contains("RCPT TO:<owner@example.test>", transcript, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Subject: Tuvima Library email test", transcript, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("No action is required", transcript, StringComparison.Ordinal);
    }

    private sealed class AccountClientFactory(string email) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new AccountHandler(email))
        {
            BaseAddress = new Uri("https://engine.example.test"),
        };
    }

    private sealed class AccountHandler(string email) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("/access/self-service", request.RequestUri?.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new AccountSelfServiceResponse(
                    Guid.NewGuid(), email, false, Guid.NewGuid(), Guid.NewGuid(), [], ["password"])),
            });
        }
    }

    private static async Task<string> ReceiveOneMessageAsync(TcpListener listener, CancellationToken ct)
    {
        using var client = await listener.AcceptTcpClientAsync(ct);
        await using var stream = client.GetStream();
        using var reader = new StreamReader(stream, leaveOpen: true);
        await using var writer = new StreamWriter(stream, leaveOpen: true)
        {
            AutoFlush = true,
            NewLine = "\r\n",
        };
        var transcript = new List<string>();
        await writer.WriteLineAsync("220 localhost ESMTP ready");

        while (await reader.ReadLineAsync(ct) is { } line)
        {
            transcript.Add(line);
            if (line.StartsWith("EHLO", StringComparison.OrdinalIgnoreCase))
            {
                await writer.WriteLineAsync("250-localhost");
                await writer.WriteLineAsync("250 8BITMIME");
            }
            else if (line.StartsWith("HELO", StringComparison.OrdinalIgnoreCase) ||
                     line.StartsWith("MAIL FROM", StringComparison.OrdinalIgnoreCase) ||
                     line.StartsWith("RCPT TO", StringComparison.OrdinalIgnoreCase))
            {
                await writer.WriteLineAsync("250 OK");
            }
            else if (line.Equals("DATA", StringComparison.OrdinalIgnoreCase))
            {
                await writer.WriteLineAsync("354 End data with <CR><LF>.<CR><LF>");
                while (await reader.ReadLineAsync(ct) is { } bodyLine && bodyLine != ".")
                {
                    transcript.Add(bodyLine);
                }

                await writer.WriteLineAsync("250 queued");
            }
            else if (line.Equals("QUIT", StringComparison.OrdinalIgnoreCase))
            {
                await writer.WriteLineAsync("221 bye");
                break;
            }
            else
            {
                await writer.WriteLineAsync("250 OK");
            }
        }

        return string.Join('\n', transcript);
    }
}
