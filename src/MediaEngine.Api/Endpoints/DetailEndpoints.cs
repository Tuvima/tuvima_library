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
            string? seriesId,
            DetailComposerService composer,
            CancellationToken ct) =>
        {
            if (!DetailComposerService.TryParseEntityType(entityType, out var parsedType))
                return Results.BadRequest(new { message = $"Unsupported detail entity type '{entityType}'." });

            var presentationContext = DetailComposerService.ParseContext(context);
            var detail = await composer.BuildAsync(parsedType, id, presentationContext, ct, seriesId);
            return detail is null ? Results.NotFound() : Results.Ok(detail);
        })
        .WithName("GetDetailPage")
        .WithSummary("Returns the unified Tuvima detail-page model for media and related entities.")
        .Produces<DetailPageViewModel>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAnyRole();

        group.MapPut("/{entityType}/{id:guid}/series-default", async (
            string entityType,
            Guid id,
            SetDefaultSeriesRequest request,
            DetailComposerService composer,
            ICanonicalValueRepository canonicalValues,
            CancellationToken ct) =>
        {
            if (!DetailComposerService.TryParseEntityType(entityType, out var parsedType))
                return Results.BadRequest(new { message = $"Unsupported detail entity type '{entityType}'." });

            var seriesId = NormalizeSeriesId(request.SeriesId);
            if (string.IsNullOrWhiteSpace(seriesId))
                return Results.BadRequest(new { message = "A valid series QID is required." });

            var detail = await composer.BuildAsync(parsedType, id, DetailPresentationContext.Default, ct);
            var matchingSeries = detail?.SeriesPlacement?.AvailableSeries.FirstOrDefault(option =>
                string.Equals(NormalizeSeriesId(option.SeriesId), seriesId, StringComparison.OrdinalIgnoreCase));
            if (matchingSeries is null)
                return Results.BadRequest(new { message = "The selected series is not a valid same-media series for this item." });

            var now = DateTimeOffset.UtcNow;
            await canonicalValues.UpsertBatchAsync(
            [
                new CanonicalValue
                {
                    EntityId = id,
                    Key = "default_series_qid",
                    Value = seriesId,
                    LastScoredAt = now,
                    WinningProviderId = WellKnownProviders.UserManual,
                },
                new CanonicalValue
                {
                    EntityId = id,
                    Key = "default_series_label",
                    Value = string.IsNullOrWhiteSpace(request.SeriesTitle)
                        ? matchingSeries.SeriesTitle
                        : request.SeriesTitle.Trim(),
                    LastScoredAt = now,
                    WinningProviderId = WellKnownProviders.UserManual,
                },
            ], ct);

            return Results.NoContent();
        })
        .WithName("SetDetailDefaultSeries")
        .WithSummary("Sets the global default same-media series for a work.")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .RequireAdminOrCurator();

        return app;
    }

    private static string? NormalizeSeriesId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        var slashIndex = trimmed.LastIndexOf('/');
        if (slashIndex >= 0)
            trimmed = trimmed[(slashIndex + 1)..];

        return trimmed.Length > 1 && trimmed[0] is 'Q' && trimmed.Skip(1).All(char.IsDigit)
            ? trimmed
            : null;
    }
}
