using MediaEngine.Contracts.Details;

namespace MediaEngine.Web.Components.Details;

/// <summary>
/// Describes the API request and presentation copy for a routed detail surface.
/// Route components remain responsible only for translating their route/query
/// parameters into one of these requests.
/// </summary>
public sealed record DetailRouteRequest
{
    private DetailRouteRequest(
        DetailEntityType? entityType,
        Guid entityId,
        DetailPresentationContext presentationContext,
        string pageTitleFallback,
        string productTitle,
        string notFoundTitle,
        string notFoundMessage,
        string? containerId = null,
        DetailRouteOriginNavigation? originNavigation = null)
    {
        EntityType = entityType;
        EntityId = entityId;
        PresentationContext = presentationContext;
        PageTitleFallback = pageTitleFallback;
        ProductTitle = productTitle;
        NotFoundTitle = notFoundTitle;
        NotFoundMessage = notFoundMessage;
        ContainerId = containerId;
        OriginNavigation = originNavigation;
    }

    public DetailEntityType? EntityType { get; }
    public Guid EntityId { get; }
    public DetailPresentationContext PresentationContext { get; }
    public string PageTitleFallback { get; }
    public string ProductTitle { get; }
    public string NotFoundTitle { get; }
    public string NotFoundMessage { get; }
    public string? ContainerId { get; }
    public DetailRouteOriginNavigation? OriginNavigation { get; }
    public bool CanLoad => EntityType.HasValue;

    public static DetailRouteRequest ForBook(Guid id, string? mode)
    {
        var context = string.Equals(mode, "listen", StringComparison.OrdinalIgnoreCase)
            ? DetailPresentationContext.Listen
            : DetailPresentationContext.Read;

        // Work is the preferred read route because it can collapse ebook +
        // audiobook formats into one canonical detail page.
        return new(
            DetailEntityType.Work,
            id,
            context,
            pageTitleFallback: "Book",
            productTitle: "Tuvima Library",
            notFoundTitle: "Book not found",
            notFoundMessage: "This title could not be loaded from your library.");
    }

    public static DetailRouteRequest ForMovie(Guid workId) =>
        new(
            DetailEntityType.Movie,
            workId,
            DetailPresentationContext.Watch,
            pageTitleFallback: "Movie",
            productTitle: "Tuvima",
            notFoundTitle: "Movie not found",
            notFoundMessage: "This title could not be loaded from your library.");

    public static DetailRouteRequest ForTvShow(Guid collectionId, Guid? episodeId)
    {
        var isEpisode = episodeId.HasValue;
        return new(
            isEpisode ? DetailEntityType.TvEpisode : DetailEntityType.TvShow,
            episodeId ?? collectionId,
            DetailPresentationContext.Watch,
            pageTitleFallback: "TV Show",
            productTitle: "Tuvima",
            notFoundTitle: isEpisode ? "Episode not found" : "TV show not found",
            notFoundMessage: isEpisode
                ? "This episode could not be loaded from your library."
                : "This show could not be loaded from your library.",
            containerId: isEpisode ? collectionId.ToString("D") : null);
    }

    public static DetailRouteRequest ForUnified(string entityType, Guid id, string? contextValue)
    {
        var context = ParseContext(contextValue);
        return new(
            TryParseEntityType(entityType, out var parsedEntityType) ? parsedEntityType : null,
            id,
            context,
            pageTitleFallback: "Details",
            productTitle: "Tuvima Library",
            notFoundTitle: "Detail page not found",
            notFoundMessage: "This item could not be loaded from your library.",
            originNavigation: OriginFor(context));
    }

    private static bool TryParseEntityType(string value, out DetailEntityType entityType)
    {
        var normalized = value
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal);

        if (normalized.Contains("podcast", StringComparison.OrdinalIgnoreCase))
        {
            entityType = default;
            return false;
        }

        return Enum.TryParse(normalized, ignoreCase: true, out entityType)
            && entityType != DetailEntityType.TvEpisode;
    }

    private static DetailPresentationContext ParseContext(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return DetailPresentationContext.Default;

        var normalized = value
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal);

        return Enum.TryParse(normalized, ignoreCase: true, out DetailPresentationContext context)
            ? context
            : DetailPresentationContext.Default;
    }

    private static DetailRouteOriginNavigation OriginFor(DetailPresentationContext context) =>
        context switch
        {
            DetailPresentationContext.Listen => new("Back to Listen", "/listen"),
            DetailPresentationContext.Watch => new("Back to Watch", "/watch"),
            DetailPresentationContext.Read or DetailPresentationContext.Comics => new("Back to Read", "/read"),
            _ => new("Back", "/"),
        };
}

public sealed record DetailRouteOriginNavigation(string Label, string FallbackRoute);
