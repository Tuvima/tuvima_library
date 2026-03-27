namespace MediaEngine.Domain.Enums;

/// <summary>
/// Controls how a provider handles language when querying its API.
/// Configured per-provider in <c>config/providers/{name}.json</c>.
/// </summary>
public enum LanguageStrategy
{
    /// <summary>
    /// Always query in the source language (English). Best for providers
    /// with poor or inconsistent localization (e.g. Google Books, Open Library).
    /// </summary>
    Source = 0,

    /// <summary>
    /// Query in the user's configured metadata language. Best for providers
    /// with excellent community translations (e.g. TMDB, Apple API).
    /// </summary>
    Localized = 1,

    /// <summary>
    /// Query twice — once in the metadata language, once in English — and merge
    /// results. Best accuracy but doubles API calls. Used by Wikidata by default.
    /// </summary>
    Both = 2,
}
