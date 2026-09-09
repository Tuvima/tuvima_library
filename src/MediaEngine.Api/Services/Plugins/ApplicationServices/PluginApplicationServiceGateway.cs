using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MediaEngine.Contracts.Plugins;
using MediaEngine.Domain.Authorization;
using MediaEngine.Domain.Contracts;

namespace MediaEngine.Api.Services.Plugins.ApplicationServices;

internal sealed record PluginApplicationOperationDescriptor(
    string OperationId,
    string PluginId,
    string DisplayName,
    string Description,
    PermissionDefinition Permission,
    Type RequestType,
    Type ResponseType,
    int MaxRequestBytes,
    int MaxResponseBytes,
    TimeSpan Timeout);

internal interface IPluginApplicationOperation
{
    PluginApplicationOperationDescriptor Descriptor { get; }
    bool IsAvailable(out string? reason);
    ValueTask<object> InvokeAsync(JsonElement payload, CancellationToken ct);
}

public interface IPluginApplicationPermissionAvailability
{
    bool TryGetAvailability(string permissionId, out bool isAvailable, out string? unavailableReason);
}

internal sealed class PluginApplicationOperationRegistry : IPluginApplicationPermissionAvailability
{
    private readonly IReadOnlyDictionary<string, IPluginApplicationOperation> _operations;

    public PluginApplicationOperationRegistry(IEnumerable<IPluginApplicationOperation> operations)
    {
        var values = operations.ToArray();
        foreach (var operation in values)
        {
            Validate(operation.Descriptor);
        }

        var duplicate = values.GroupBy(value => value.Descriptor.OperationId, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException($"Duplicate plugin application operation '{duplicate.Key}'.");
        }

        _operations = values.ToDictionary(value => value.Descriptor.OperationId, StringComparer.Ordinal);
    }

    public IReadOnlyList<PluginApplicationOperationDto> List() => _operations.Values
        .OrderBy(value => value.Descriptor.OperationId, StringComparer.Ordinal)
        .Select(value =>
        {
            var available = value.IsAvailable(out var reason);
            var descriptor = value.Descriptor;
            return new PluginApplicationOperationDto(
                descriptor.OperationId,
                descriptor.PluginId,
                descriptor.DisplayName,
                descriptor.Description,
                descriptor.Permission.Id.Value,
                descriptor.RequestType.Name,
                descriptor.ResponseType.Name,
                available,
                available ? null : reason ?? "The plugin service is unavailable.");
        }).ToArray();

    public IPluginApplicationOperation GetRequired(string operationId) =>
        _operations.TryGetValue(operationId, out var operation)
            ? operation
            : throw new PluginApplicationGatewayException(PluginApplicationGatewayFailure.UnknownOperation, "Plugin application operation not found.");

    public bool TryGetAvailability(string permissionId, out bool isAvailable, out string? unavailableReason)
    {
        if (!_operations.TryGetValue(permissionId, out var operation))
        {
            isAvailable = false;
            unavailableReason = null;
            return false;
        }
        isAvailable = operation.IsAvailable(out unavailableReason);
        return true;
    }

    private static void Validate(PluginApplicationOperationDescriptor descriptor)
    {
        if (descriptor.Permission.Provenance != PermissionProvenance.Plugin
            || !string.Equals(descriptor.Permission.PluginId, descriptor.PluginId, StringComparison.Ordinal)
            || !string.Equals(descriptor.Permission.Id.Value, descriptor.OperationId, StringComparison.Ordinal)
            || !descriptor.OperationId.StartsWith($"plugin.{descriptor.PluginId}.", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Plugin operation provenance and permission namespace must match its manifest plugin identity.");
        }

        if (descriptor.MaxRequestBytes is < 1 or > 65_536
            || descriptor.MaxResponseBytes is < 1 or > 1_048_576
            || descriptor.Timeout < TimeSpan.FromSeconds(1)
            || descriptor.Timeout > TimeSpan.FromSeconds(30))
        {
            throw new InvalidOperationException("Plugin operation limits exceed the gateway bounds.");
        }
    }
}

internal sealed class PluginApplicationServiceGateway(
    PluginApplicationOperationRegistry registry,
    IAuthorizationEvaluator authorization)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public IReadOnlyList<PluginApplicationOperationDto> List() => registry.List();

    public async Task<JsonElement> InvokeAsync(
        RequestAuthority authority,
        string operationId,
        ReadOnlyMemory<byte> payload,
        CancellationToken ct = default)
    {
        var operation = registry.GetRequired(operationId);
        var decision = await authorization.EvaluateAsync(
            authority,
            new AuthorizationRequirement(ApplicationPermission: operation.Descriptor.Permission.Id),
            null,
            ct).ConfigureAwait(false);
        if (!decision.IsAllowed)
        {
            throw new PluginApplicationGatewayException(PluginApplicationGatewayFailure.Forbidden, $"Application permission denied: {decision.DenialReason}.");
        }

        if (!operation.IsAvailable(out var reason))
        {
            throw new PluginApplicationGatewayException(PluginApplicationGatewayFailure.Unavailable, reason ?? "The plugin service is unavailable.");
        }

        if (payload.Length > operation.Descriptor.MaxRequestBytes)
        {
            throw new PluginApplicationGatewayException(PluginApplicationGatewayFailure.PayloadTooLarge, "The plugin service request is too large.");
        }

        JsonElement body;
        try
        {
            using var document = JsonDocument.Parse(payload, new JsonDocumentOptions { MaxDepth = 16 });
            body = document.RootElement.Clone();
        }
        catch (JsonException exception)
        {
            throw new PluginApplicationGatewayException(PluginApplicationGatewayFailure.InvalidPayload, "The plugin service request is not valid JSON.", exception);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(operation.Descriptor.Timeout);
        object response;
        try
        {
            response = await operation.InvokeAsync(body, timeout.Token).ConfigureAwait(false);
        }
        catch (JsonException exception)
        {
            throw new PluginApplicationGatewayException(PluginApplicationGatewayFailure.InvalidPayload, "The plugin service request does not match its typed contract.", exception);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new PluginApplicationGatewayException(PluginApplicationGatewayFailure.Timeout, "The plugin service operation timed out.");
        }

        var serialized = JsonSerializer.SerializeToElement(response, operation.Descriptor.ResponseType, JsonOptions);
        if (Encoding.UTF8.GetByteCount(serialized.GetRawText()) > operation.Descriptor.MaxResponseBytes)
        {
            throw new PluginApplicationGatewayException(PluginApplicationGatewayFailure.ResponseTooLarge, "The plugin service response exceeded its configured limit.");
        }

        return serialized;
    }

    internal static TRequest ReadRequest<TRequest>(JsonElement payload) where TRequest : class =>
        payload.Deserialize<TRequest>(JsonOptions)
        ?? throw new JsonException("The plugin service request body is required.");
}

internal enum PluginApplicationGatewayFailure
{
    UnknownOperation,
    Forbidden,
    Unavailable,
    InvalidPayload,
    PayloadTooLarge,
    ResponseTooLarge,
    Timeout,
}

internal sealed class PluginApplicationGatewayException : Exception
{
    public PluginApplicationGatewayException(
        PluginApplicationGatewayFailure failure,
        string message,
        Exception? innerException = null) : base(message, innerException) => Failure = failure;

    public PluginApplicationGatewayFailure Failure { get; }
}
