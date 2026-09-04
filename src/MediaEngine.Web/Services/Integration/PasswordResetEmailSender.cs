using System.Net;
using System.Net.Mail;
using MediaEngine.Domain.Configuration;

namespace MediaEngine.Web.Services.Integration;

public sealed class PasswordResetEmailSender(PasswordResetDeliverySettings settings, ILogger<PasswordResetEmailSender> logger)
{
    public bool IsConfigured => settings.Mode.Equals("Smtp", StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(settings.SmtpHost)
        && !string.IsNullOrWhiteSpace(settings.FromAddress)
        && Uri.TryCreate(settings.PublicBaseUrl, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || uri.IsLoopback);

    public async Task<bool> SendAsync(string email, string token, CancellationToken ct)
    {
        if (!IsConfigured) return false;
        var baseUri = new Uri(settings.PublicBaseUrl.TrimEnd('/') + "/", UriKind.Absolute);
        var resetUri = new Uri(baseUri, $"auth/reset?token={Uri.EscapeDataString(token)}");
        return await SendMessageAsync(
            email,
            "Reset your Tuvima Library password",
            $"A password reset was requested for your Tuvima Library account.\n\nOpen this link within 30 minutes:\n{resetUri}\n\nIf you did not request this, you can ignore this email.",
            ct).ConfigureAwait(false);
    }

    public Task<bool> SendTestAsync(string email, CancellationToken ct) => SendMessageAsync(
        email,
        "Tuvima Library email test",
        "Email delivery is configured correctly for this Tuvima Library server. No action is required.",
        ct);

    private async Task<bool> SendMessageAsync(string email, string subject, string body, CancellationToken ct)
    {
        if (!IsConfigured) return false;
        using var message = new MailMessage
        {
            From = new MailAddress(settings.FromAddress, settings.FromName),
            Subject = subject,
            Body = body,
            IsBodyHtml = false,
        };
        message.To.Add(new MailAddress(email));
        using var client = new SmtpClient(settings.SmtpHost, settings.SmtpPort)
        {
            EnableSsl = settings.UseStartTls,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            UseDefaultCredentials = false,
        };
        if (!string.IsNullOrWhiteSpace(settings.Username))
        {
            client.Credentials = new NetworkCredential(settings.Username, settings.Password);
        }
        try
        {
            await client.SendMailAsync(message, ct).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is SmtpException or InvalidOperationException or FormatException)
        {
            logger.LogError(ex, "Password reset email delivery failed.");
            return false;
        }
    }
}
