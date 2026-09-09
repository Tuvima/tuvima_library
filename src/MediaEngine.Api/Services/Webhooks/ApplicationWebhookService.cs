using System.Security.Cryptography;
using System.Text.Json;
using MediaEngine.Api.Services.Events;
using MediaEngine.Contracts.Authentication;
using MediaEngine.Domain.Authorization;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Events;
using MediaEngine.Domain.Models;
using MediaEngine.Storage;
using Microsoft.AspNetCore.DataProtection;

namespace MediaEngine.Api.Services.Webhooks;

internal sealed class ApplicationWebhookService(
    ApplicationWebhookRepository repository,
    IApplicationRepository applications,
    IApplicationEventRepository events,
    IApplicationEventDeliveryAuthorizer authorization,
    ApplicationEventRegistry eventTypes,
    WebhookDestinationPolicy destinations,
    IDataProtectionProvider protection,
    TimeProvider clock)
{
    public IReadOnlyList<string> EventTypes => eventTypes.ListAvailableTypes();

    public async Task<IReadOnlyList<ApplicationWebhookResponse>> ListAsync(Guid applicationId, CancellationToken ct)
    {
        await RequireApplicationAsync(applicationId, ct);
        return (await repository.ListAsync(applicationId, ct)).Select(Map).ToList();
    }

    public async Task<ApplicationWebhookSecretResponse> SaveAsync(Guid applicationId, Guid? id,
        SaveApplicationWebhookRequest request, CancellationToken ct)
    {
        await RequireApplicationAsync(applicationId, ct);
        if (request.EventTypes is null || request.EventTypes.Count is < 1 or > 32
            || request.EventTypes.Any(type => string.IsNullOrWhiteSpace(type) || !eventTypes.TryGet(type, out _)))
        {
            throw new ArgumentException("Select one or more supported event types.");
        }

        var types = request.EventTypes.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (request.IsEnabled && !await authorization.CanSubscribeAsync(applicationId, types, ct))
        {
            throw new ArgumentException("The application needs events.subscribe and read access for every selected event type.");
        }

        var uri = WebhookDestinationPolicy.Parse(request.Url, request.AllowLocalNetwork);
        await destinations.ResolveApprovedAsync(uri, request.AllowLocalNetwork, ct);
        var webhook = id.HasValue ? await RequireWebhookAsync(applicationId, id.Value, ct) : new ApplicationWebhook
        {
            Id = Guid.NewGuid(),
            ApplicationId = applicationId,
            CreatedAt = clock.GetUtcNow(),
        };
        if (id.HasValue && request.ExpectedVersion != webhook.Version)
        {
            throw new InvalidOperationException("The webhook changed. Reload before saving.");
        }

        webhook.Url = uri.AbsoluteUri;
        webhook.EventTypesJson = JsonSerializer.Serialize(types);
        webhook.IsEnabled = request.IsEnabled;
        webhook.AllowLocalNetwork = request.AllowLocalNetwork;
        webhook.UpdatedAt = clock.GetUtcNow();
        webhook.LastStatus = request.IsEnabled ? "Waiting for new events" : "Disabled";
        webhook.LastEventSequence = (await events.GetBoundsAsync(ct)).LatestSequence ?? 0;
        var secret = id.HasValue ? null : Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        if (secret is not null)
        {
            webhook.SecretCiphertext = Protector(webhook.Id).Protect(secret);
        }

        await repository.SaveAsync(webhook, !id.HasValue, ct);
        if (id.HasValue)
        {
            webhook.Version++;
        }

        return new(Map(webhook), secret);
    }

    public async Task<ApplicationWebhookSecretResponse> RotateAsync(Guid applicationId, Guid id, CancellationToken ct)
    {
        await RequireApplicationAsync(applicationId, ct);
        var webhook = await RequireWebhookAsync(applicationId, id, ct);
        var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        webhook.SecretCiphertext = Protector(id).Protect(secret);
        webhook.UpdatedAt = clock.GetUtcNow();
        webhook.LastStatus = "Signing secret rotated; previous pending deliveries cancelled";
        await repository.SaveAsync(webhook, false, ct);
        webhook.Version++;
        return new(Map(webhook), secret);
    }

    public async Task DeleteAsync(Guid applicationId, Guid id, CancellationToken ct)
    {
        await RequireApplicationAsync(applicationId, ct);
        await RequireWebhookAsync(applicationId, id, ct);
        await repository.DeleteAsync(applicationId, id, ct);
    }

    internal string RevealSecret(ApplicationWebhook webhook) => Protector(webhook.Id).Unprotect(webhook.SecretCiphertext);
    private IDataProtector Protector(Guid id) => protection.CreateProtector("Tuvima.ApplicationWebhooks.v1", id.ToString("D"));

    private async Task RequireApplicationAsync(Guid applicationId, CancellationToken ct)
    {
        var application = await applications.GetApplicationAsync(applicationId, ct)
            ?? throw new KeyNotFoundException("Application not found.");
        if (application.ApplicationType is not (ApplicationType.ServerIntegration or ApplicationType.Automation))
        {
            throw new ArgumentException("Webhooks require a server integration or automation application.");
        }
    }

    private async Task<ApplicationWebhook> RequireWebhookAsync(Guid applicationId, Guid id, CancellationToken ct)
    {
        var webhook = await repository.FindAsync(id, ct);
        return webhook?.ApplicationId == applicationId ? webhook : throw new KeyNotFoundException("Webhook not found.");
    }

    internal static ApplicationWebhookResponse Map(ApplicationWebhook webhook) => new(
        webhook.Id, webhook.ApplicationId, webhook.Url,
        JsonSerializer.Deserialize<string[]>(webhook.EventTypesJson) ?? [], webhook.AllowLocalNetwork,
        webhook.IsEnabled, webhook.Version, webhook.LastStatus, webhook.LastAttemptAt, webhook.LastSuccessAt);
}
