namespace MediaEngine.Providers.Models;

/// <summary>
/// A single result from a multi-result search query against an external provider.
///
/// Used by the <c>SearchAsync</c> multi-result path to return a list of candidates
/// for the user to choose from in the Needs Review resolution panel.
///
/// Each item carries enough information to render a result card with thumbnail,
/// title, year, description, and a provider-specific item identifier for
/// subsequent direct lookup.
/// </summary>
/// <param name="Title">The title of the matched item.</param>
/// <param name="Author">The author, artist, or creator name, if available.</param>
/// <param name="Description">A short description or summary (may be HTML-stripped).</param>
/// <param name="Year">Publication or release year, if available.</param>
/// <param name="ThumbnailUrl">URL to a thumbnail/cover image, if available.</param>
/// <param name="ProviderItemId">
/// Provider-specific identifier that can be used for direct lookup
/// (e.g. Apple Books ID, ASIN, Open Library key, Google Books volume ID).
/// </param>
/// <param name="Confidence">Match confidence score from the provider (0.0–1.0).</param>
/// <param name="ProviderName">Name of the provider that produced this result.</param>
public sealed record SearchResultItem(
    string Title,
    string? Author,
    string? Description,
    string? Year,
    string? ThumbnailUrl,
    string? ProviderItemId,
    double Confidence,
    string ProviderName);
