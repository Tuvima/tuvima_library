using MediaEngine.Api.Http;
using MediaEngine.Api.Security;
using MediaEngine.Domain.Contracts;
using ProviderConfigDto = MediaEngine.Contracts.Admin.ProviderConfigDto;
using UpsertProviderConfigRequest = MediaEngine.Contracts.Admin.UpsertProviderConfigRequest;

namespace MediaEngine.Api.Endpoints;

public static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/admin/provider-configs").WithTags("Admin").RequireEffectiveAdministrator();
        group.MapGet("/{providerId}", async (string providerId, IProviderConfigurationRepository repository, CancellationToken ct) => Results.Ok((await repository.GetAllMaskedAsync(providerId, ct)).Select(x => new ProviderConfigDto { ProviderId = x.ProviderId, Key = x.Key, Value = x.Value, IsSecret = x.IsSecret }).ToList())).Produces<List<ProviderConfigDto>>();
        group.MapMethods("/{providerId}/{configKey}", ["PUT"], async (string providerId, string configKey, UpsertProviderConfigRequest request, IProviderConfigurationRepository repository, CancellationToken ct) =>
        { if (string.IsNullOrWhiteSpace(request.Value) || request.Value == "********") { return ApiErrors.BadRequest("Provide the actual non-empty value."); } await repository.UpsertAsync(providerId, configKey, request.Value, request.IsSecret, ct); return Results.NoContent(); }).WithName("UpsertProviderSecretConfiguration").Produces(StatusCodes.Status204NoContent);
        group.MapDelete("/{providerId}/{configKey}", async (string providerId, string configKey, IProviderConfigurationRepository repository, CancellationToken ct) => { await repository.DeleteAsync(providerId, configKey, ct); return Results.NoContent(); }).WithName("DeleteProviderSecretConfiguration").Produces(StatusCodes.Status204NoContent);
        return app;
    }
}
