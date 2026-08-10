using MediaEngine.Contracts.Details;
using MediaEngine.Domain.Configuration;

namespace MediaEngine.Api.Services.Details.Internals;

internal sealed class ProviderSourceLinkResolver(
    IReadOnlyList<ProviderConfiguration> providers)
{
    public IReadOnlyList<ExternalSourceLinkViewModel> Resolve(
        IReadOnlyDictionary<string, string> identifiers,
        string? mediaType = null)
    {
        if (identifiers.Count == 0)
            return [];

        var links = new List<ExternalSourceLinkViewModel>();
        foreach (var provider in providers)
        {
            if (provider.UiMetadata?.ExternalLinks is not { Count: > 0 } configuredLinks)
                continue;

            foreach (var (identifierKey, linkConfig) in configuredLinks)
            {
                if (!identifiers.TryGetValue(identifierKey, out var identifierValue)
                    || string.IsNullOrWhiteSpace(identifierValue)
                    || string.IsNullOrWhiteSpace(linkConfig.UrlTemplate))
                {
                    continue;
                }

                var url = ExpandTemplate(
                    linkConfig.UrlTemplate,
                    identifierKey,
                    identifierValue,
                    mediaType);
                if (url is null
                    || links.Any(link => string.Equals(link.Url, url, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                links.Add(new ExternalSourceLinkViewModel
                {
                    Key = identifierKey,
                    Label = string.IsNullOrWhiteSpace(linkConfig.Label)
                        ? $"View on {provider.DisplayName ?? provider.Name}"
                        : linkConfig.Label,
                    Url = url,
                    SourceName = provider.DisplayName ?? provider.Name,
                    Tooltip = linkConfig.Tooltip,
                });
            }
        }

        return links;
    }

    private static string? ExpandTemplate(
        string template,
        string identifierKey,
        string rawValue,
        string? mediaType)
    {
        var value = rawValue.Trim();
        var templateValue = Uri.TryCreate(value, UriKind.Absolute, out var absolute)
                            && absolute.Scheme is "http" or "https"
            ? value
            : Uri.EscapeDataString(value);
        var url = template
            .Replace($"{{{identifierKey}}}", templateValue, StringComparison.OrdinalIgnoreCase)
            .Replace("{value}", templateValue, StringComparison.OrdinalIgnoreCase)
            .Replace("{media_type}", ResolveMediaTypePath(mediaType), StringComparison.OrdinalIgnoreCase);

        return Uri.TryCreate(url, UriKind.Absolute, out var resolved)
               && resolved.Scheme is "http" or "https"
            ? resolved.ToString()
            : null;
    }

    private static string ResolveMediaTypePath(string? mediaType) =>
        !string.IsNullOrWhiteSpace(mediaType)
        && mediaType.Contains("TV", StringComparison.OrdinalIgnoreCase)
            ? "tv"
            : "movie";
}
