using MediaEngine.Api.Security;
using MediaEngine.Api.Services.Details;
using MediaEngine.Contracts.Details;
using MediaEngine.Domain;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;

namespace MediaEngine.Api.Endpoints;

public static class DetailEndpoints
{
    public static IEndpointRouteBuilder MapDetailEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/details")
            .WithTags("Details");

        group.MapGet("/{entityType}/{id:guid}", async (
            string entityType,
            Guid id,
            string? context,
            string? containerId,
            Guid? profileId,
            DetailComposerService composer,
            CancellationToken ct) =>
        {
            if (!DetailComposerService.TryParseEntityType(entityType, out var parsedType))
                return Results.BadRequest(new { message = $"Unsupported detail entity type '{entityType}'." });

            var presentationContext = DetailComposerService.ParseContext(context);
            var detail = await composer.BuildAsync(parsedType, id, presentationContext, ct, containerId, profileId);
            return detail is null ? Results.NotFound() : Results.Ok(detail);
        })
        .WithName("GetDetailPage")
        .WithSummary("Returns the unified Tuvima detail-page model for media and related entities.")
        .Produces<DetailPageViewModel>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAnyRole();

        group.MapPut("/{entityType}/{id:guid}/sequence-default", async (
            string entityType,
            Guid id,
            SetDefaultSequenceRequest request,
            DetailComposerService composer,
            ICanonicalValueRepository canonicalValues,
            CancellationToken ct) =>
        {
            if (!DetailComposerService.TryParseEntityType(entityType, out var parsedType))
                return Results.BadRequest(new { message = $"Unsupported detail entity type '{entityType}'." });

            var containerId = NormalizeContainerId(request.ContainerId);
            if (string.IsNullOrWhiteSpace(containerId))
                return Results.BadRequest(new { message = "A valid sequence container is required." });

            var detail = await composer.BuildAsync(parsedType, id, DetailPresentationContext.Default, ct);
            var matchingContainer = detail?.SequencePlacement?.AvailableContainers.FirstOrDefault(option =>
                ContainerMatches(option, containerId));
            if (matchingContainer is null)
                return Results.BadRequest(new { message = "The selected container is not valid for this item." });

            var now = DateTimeOffset.UtcNow;
            await canonicalValues.UpsertBatchAsync(
            [
                new CanonicalValue
                {
                    EntityId = id,
                    Key = "default_sequence_container_id",
                    Value = containerId,
                    LastScoredAt = now,
                    WinningProviderId = WellKnownProviders.UserManual,
                },
                new CanonicalValue
                {
                    EntityId = id,
                    Key = "default_sequence_container_label",
                    Value = string.IsNullOrWhiteSpace(request.ContainerTitle)
                        ? matchingContainer.ContainerTitle
                        : request.ContainerTitle.Trim(),
                    LastScoredAt = now,
                    WinningProviderId = WellKnownProviders.UserManual,
                },
            ], ct);

            return Results.NoContent();
        })
        .WithName("SetDetailDefaultSequence")
        .WithSummary("Sets the default same-media sequence container for a work.")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .RequireAdminOrCurator();

        return app;
    }

    private static string? NormalizeContainerId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        var slashIndex = trimmed.LastIndexOf('/');
        if (slashIndex >= 0)
            trimmed = trimmed[(slashIndex + 1)..];

        return trimmed.Length > 1 && trimmed[0] is 'Q' && trimmed.Skip(1).All(char.IsDigit)
            ? trimmed
            : trimmed;
    }

    private static bool ContainerMatches(SequenceContainerOptionViewModel option, string containerId)
    {
        var normalized = NormalizeContainerId(containerId);
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        return string.Equals(NormalizeContainerId(option.ContainerId), normalized, StringComparison.OrdinalIgnoreCase)
            || string.Equals(NormalizeContainerId(option.SourceContainerId), normalized, StringComparison.OrdinalIgnoreCase)
            || option.EquivalentContainerIds.Any(alias =>
                string.Equals(NormalizeContainerId(alias), normalized, StringComparison.OrdinalIgnoreCase));
    }
}
