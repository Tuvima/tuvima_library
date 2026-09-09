using MediaEngine.Domain.Authorization;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Events;

namespace MediaEngine.Api.Services.Events;

public sealed class ApplicationEventDeliveryAuthorizer(
    IApplicationRepository applications,
    IPermissionRegistry permissions,
    ApplicationEventRegistry events) : IApplicationEventDeliveryAuthorizer
{
    public async ValueTask<bool> CanSubscribeAsync(
        Guid applicationId,
        IReadOnlyList<string> eventTypes,
        CancellationToken ct = default)
    {
        if (applicationId == Guid.Empty || eventTypes.Count is < 1 or > 32)
        {
            return false;
        }

        var definitions = new List<ApplicationEventDefinition>(eventTypes.Count);
        foreach (var eventType in eventTypes.Distinct(StringComparer.Ordinal))
        {
            if (!events.TryGet(eventType, out var definition))
            {
                return false;
            }

            definitions.Add(definition);
        }
        var application = await applications.GetApplicationAsync(applicationId, ct).ConfigureAwait(false);
        return application is not null && await HasPermissionsAsync(application, definitions, ct).ConfigureAwait(false);
    }

    public async ValueTask<bool> CanDeliverAsync(
        Guid applicationId,
        StoredApplicationEvent value,
        CancellationToken ct = default)
    {
        if (applicationId == Guid.Empty || !events.TryGet(value.EventType, out var definition))
        {
            return false;
        }

        if (definition.IsLibraryScoped && value.Subject.LibraryId is null)
        {
            return false;
        }

        if (definition.IsProfileScoped && value.Subject.ProfileId is null)
        {
            return false;
        }

        var application = await applications.GetApplicationAsync(applicationId, ct).ConfigureAwait(false);
        return application is not null && await HasPermissionsAsync(application, [definition], ct).ConfigureAwait(false);
    }

    private async ValueTask<bool> HasPermissionsAsync(
        MediaEngine.Domain.Entities.Application application,
        IReadOnlyList<ApplicationEventDefinition> definitions,
        CancellationToken ct)
    {
        if (!application.IsEnabled ||
            application.ApplicationType is not (ApplicationType.ServerIntegration or ApplicationType.Automation))
        {
            return false;
        }

        var required = new HashSet<ApplicationPermissionId> { ApplicationPermissionIds.EventsSubscribe };
        foreach (var definition in definitions)
        {
            required.Add(definition.ReadPermission);
            if (definition.IsLibraryScoped)
            {
                required.Add(ApplicationPermissionIds.LibraryRead);
            }
        }

        foreach (var permission in required)
        {
            if (!permissions.TryGet(permission, out var registered) || !registered.IsAvailable ||
                !registered.ApplicationTypes.Contains(application.ApplicationType))
            {
                return false;
            }
        }

        if (application.IsAdministrator)
        {
            return true;
        }

        var granted = await applications.GetApplicationPermissionsAsync(application.Id, ct).ConfigureAwait(false);
        return required.All(granted.Contains);
    }
}
