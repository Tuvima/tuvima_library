using MediaEngine.Api.Http;
using MediaEngine.Api.Security;
using MediaEngine.Api.Services.Plugins.ApplicationServices;
using MediaEngine.Contracts.Plugins;
using MediaEngine.Domain.Authorization;

namespace MediaEngine.Api.Endpoints;

internal static class PluginApplicationServiceEndpoints
{
    private const int GatewayRequestLimit = 65_536;

    internal static RouteGroupBuilder MapPluginApplicationServiceEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/plugin-services")
            .WithTags("Plugin Application Services")
            .RequireAuthorization(AuthPolicies.Authenticated);

        group.MapGet("/operations", (PluginApplicationServiceGateway gateway) => Results.Ok(gateway.List()))
            .WithName("ListPluginApplicationOperations")
            .WithSummary("List registered plugin application operations and live availability.")
            .Produces<IReadOnlyList<PluginApplicationOperationDto>>()
            .RequireAdministratorOrApplication(ApplicationPermissionIds.PluginsRead);

        group.MapPost("/{operationId}", InvokeAsync)
            .WithName("InvokePluginApplicationOperation")
            .WithSummary("Invoke a registered plugin operation through its typed, permission-bound gateway.")
            .Accepts<DiscoverUniverseLoreSourcesRequest>("application/json")
            .Produces<DiscoverUniverseLoreSourcesResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .ProducesProblem(StatusCodes.Status504GatewayTimeout);

        return group;
    }

    private static async Task<IResult> InvokeAsync(
        string operationId,
        HttpContext http,
        IRequestAuthorityResolver authorityResolver,
        PluginApplicationServiceGateway gateway,
        CancellationToken ct)
    {
        try
        {
            var authority = await authorityResolver.ResolveAsync(http, ct).ConfigureAwait(false);
            var payload = await ReadBoundedAsync(http.Request.Body, GatewayRequestLimit, ct).ConfigureAwait(false);
            var response = await gateway.InvokeAsync(authority, operationId, payload, ct).ConfigureAwait(false);
            return Results.Ok(response);
        }
        catch (PluginApplicationGatewayException exception)
        {
            return exception.Failure switch
            {
                PluginApplicationGatewayFailure.UnknownOperation => ApiErrors.NotFound(exception.Message),
                PluginApplicationGatewayFailure.Forbidden => ApiErrors.Forbidden(exception.Message),
                PluginApplicationGatewayFailure.InvalidPayload => ApiErrors.BadRequest(exception.Message),
                PluginApplicationGatewayFailure.PayloadTooLarge => ApiErrors.Problem(StatusCodes.Status413PayloadTooLarge, "Payload too large.", exception.Message),
                PluginApplicationGatewayFailure.ResponseTooLarge => ApiErrors.Unprocessable(exception.Message),
                PluginApplicationGatewayFailure.Unavailable => ApiErrors.Problem(StatusCodes.Status503ServiceUnavailable, "Plugin service unavailable.", exception.Message),
                PluginApplicationGatewayFailure.Timeout => ApiErrors.Problem(StatusCodes.Status504GatewayTimeout, "Plugin service timeout.", exception.Message),
                _ => ApiErrors.BadRequest(exception.Message),
            };
        }
    }

    private static async Task<ReadOnlyMemory<byte>> ReadBoundedAsync(Stream stream, int maxBytes, CancellationToken ct)
    {
        using var output = new MemoryStream(Math.Min(maxBytes, 4 * 1024));
        var buffer = new byte[4 * 1024];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, ct).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            if (output.Length + read > maxBytes)
            {
                throw new PluginApplicationGatewayException(PluginApplicationGatewayFailure.PayloadTooLarge, "The plugin service request is too large.");
            }

            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }
}
