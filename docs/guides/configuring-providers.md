---
title: "How to Configure Metadata Providers"
summary: "Set up provider keys, priorities, and defaults so enrichment behaves the way you expect."
audience: "user"
category: "guide"
product_area: "providers"
tags:
  - "providers"
  - "api-keys"
  - "configuration"
---

# How to Configure Metadata Providers

This guide explains what metadata providers are, which ones work out of the box, which ones need a key, and how to control how they're used.

---

## What providers do

When the Engine identifies a file in your library, it reaches out to external sources to gather extra information: cover art, descriptions, ratings, cast and crew, identifiers like ISBNs or TMDB IDs, and more. These external sources are called **metadata providers**.

The Providers screen is organized around the same numbered stages shown on the Ingestion page:

1. **Retail providers** (Stage 3: Retail Match) - run after file details are read. These gather practical information: cover art, descriptions, ratings, and identifiers. The Engine uses this data both to enrich your library and to improve its confidence in identifying what the file is.

2. **Wikidata** (Stage 4: Wikidata) - runs after retail lookup, using identifiers gathered in Stage 3. Wikidata is the authority for canonical structured data: the author's full name, the official series name, genre classifications, director credits, and so on. Wikidata is always free to use and requires no key.

3. **Enrichment and artwork providers** (Stages 6-8) - run after identity is known. These providers add people, relationships, fan art, synced lyrics, subtitles, and periodic refresh data. Rich artwork providers such as Fanart.tv are Stage 8, not the first cover/poster pass.

---

## Providers that work out of the box

These providers require no account, no sign-up, and no configuration. They are active as soon as you install Tuvima Library.

| Provider | What it supplies |
|---|---|
| **Wikidata** | Canonical identity, structured metadata, people, series, genre |
| **Wikipedia** | Plain-language descriptions |
| **Apple API** | Cover art, descriptions, ratings (books, audiobooks, music) |
| **LRCLIB** | Lyrics and timed lyrics for music |

These providers are enabled by default where their config marks them active. Open Library and MusicBrainz configs are retained for future or explicit use, but they are disabled in the normal runtime setup.

---

## Providers that require an API key

Some providers require you to create a free account and obtain an API key before they can be used. The key lets the provider's service know the request is coming from your installation.

### TMDB (The Movie Database)

TMDB supplies cover art, descriptions, cast and crew, ratings, and backdrops for movies and TV.

1. Go to `https://www.themoviedb.org/settings/api` and create a free account.
2. Request an API key (choose "Developer" use type).
3. Copy the key.
4. In the Dashboard, go to **Settings -> Providers** and keep **Retail Lookup** selected.
5. Find TMDB in the Provider Library and paste your key into the API Key field in the settings panel.
6. Click **Save Provider**.

### Comic Vine

Comic Vine supplies metadata for comics - issue numbers, story arcs, publishers, and character information.

1. Go to `https://comicvine.gamespot.com/api/` and create a free account.
2. Click **Get API Key**.
3. Copy the key.
4. In the Dashboard, go to **Settings -> Providers** and keep **Retail Lookup** selected.
5. Find Comic Vine in the Provider Library and paste your key into the API Key field in the settings panel.
6. Click **Save Provider**.

---

## Where provider keys are stored

Provider configuration is file based. The Engine reads provider definitions from `config/providers/*.json` and also applies secret overlays from `config/secrets/{provider}.json` when those files exist.

That means a provider file such as `config/providers/tmdb.json` may show an empty `http_client.api_key` while the effective runtime key is still present in `config/secrets/tmdb.json`. Do not treat a blank base provider file as proof that a key was deleted. Check the matching file under `config/secrets/` as well.

When you save provider settings from the Dashboard, mutable provider settings are written back to the provider config file. For manual edits, keep long-lived credentials in `config/secrets/{provider}.json` so the base provider definition can stay shareable and the key can be rotated independently.

---

## Retail lookup inputs by media type

Retail lookup is Stage 3. It searches the configured retail provider, then scores returned candidates against local file evidence. Open Library and MusicBrainz configs exist, but they are disabled in the normal runtime path.

| Media type | Active retail provider | Lookup inputs sent to provider | Candidate scoring metrics | Bridge IDs produced for Wikidata |
|---|---|---|---|---|
| Books | Apple API | ISBN exact lookup when `isbn` exists; Apple Books ID lookup when `apple_books_id` exists; otherwise ebook search using `title` plus `author` when available. | Title, author, year/date, media format, description/publisher/page-count cross-checks, and cover similarity when available. | `apple_books_id`; existing file ISBN/ASIN evidence can also be carried as bridge evidence. |
| Audiobooks | Apple API | Apple Books ID lookup when available; otherwise audiobook search using `title` plus `author` when available. | Title, author, year/date, narrator-in-description, duration when available, media format, and cover similarity. | `apple_books_id`; existing `isbn` or `asin` evidence can also be carried as bridge evidence. |
| Music | Apple API | Grouped by artist and album. The worker first searches by track title and artist to discover an Apple collection, can search by artist plus album, and can use `apple_music_collection_id` for album lookup. | Track title, artist/composer/author, album, year/date, track number, duration, media format, and cover similarity. | `apple_music_id`, `apple_music_collection_id`, `apple_artist_id`. |
| Movies | TMDB | Movie search using `title`; `year` is included when known; requests include the configured TMDB API key. | Title, year, director/writer/author evidence when present locally, media format, genre/description cross-checks, and poster similarity. | `tmdb_id` mapped as a movie identifier. |
| TV | TMDB | Grouped by show and season. The worker searches by `show_name` or `series`, includes a year hint if any episode has one, then fetches the TMDB season episode list using `season_number`. | Episode title, show/series, season number, episode number, year, media format, and poster/still similarity. Exact show/season/episode agreement is required for confident grouped acceptance. | `tmdb_id` mapped as a TV-series identifier. |
| Comics | Comic Vine | Issue search using `title`; volume search using `series`; requests include the configured Comic Vine API key. | Title, series, issue number or series position, writer/author/illustrator evidence, year, media format, and cover similarity. | `comic_vine_id`; existing ISBN/GCD evidence can also be carried as bridge evidence. |

Retail confidence uses the configured weights in `config/hydration.json`: title `0.45`, creator `0.35`, year `0.10`, and format `0.10`. A score of `0.90` or higher can auto-accept, `0.65` to below `0.90` goes to review, and lower scores are treated as no safe retail match.

---

## Wikidata inputs by media type

Wikidata is Stage 4. It is intentionally gated behind Stage 3: the Wikidata bridge worker only processes items that reached `RetailMatched` or `RetailMatchedNeedsReview`. Items with no safe retail match are not sent to Wikidata as a broad title-only fallback.

Stage 4 requires at least one real bridge ID. Title, creator, year, series, album, artist, and language hints help the resolver rank or roll up results, but they do not bypass the bridge-ID requirement.

Wikidata relationship targets are classified before they become shelves. Ordered series, album releases, TV shows/seasons, comic series, and manga series can become immediate lane shelves. Franchises and universes are broader relationship context, and Wikimedia list articles or publisher/production lists are diagnostics only. A fresh ingestion uses this classification immediately; existing persisted rows are not backfilled or repaired in place.

| Media type | Wikidata bridge IDs used | Hints sent with the bridge request | Wikidata media kind and filtering | Edition/rollup behavior |
|---|---|---|---|---|
| Books | `isbn`, `isbn_13`, `isbn_10`, `asin`, `apple_books_id`, `open_library_id`, `goodreads_id` when present. | Title, author, year, language. | Book/literary work classes; excludes people, films, TV, and music classes. | Edition-aware; returns the work and edition when available. |
| Audiobooks | `apple_books_id`, `isbn`, `asin`, `audible_id`, MusicBrainz IDs when present. | Title, author, year, language. | Audiobook and written-work classes. | Edition-aware and prefers audiobook edition identity when available. |
| Music | `apple_music_id`, `apple_music_collection_id`, `apple_artist_id`, and MusicBrainz IDs when present. | Album title, artist, track title, author fallback, year, language. | Music album when album title is known; otherwise music work. | Edition-aware; album IDs roll up tracks to the album/work identity. |
| Movies | `tmdb_id`, `imdb_id`, Apple TV movie IDs when present. | Title, author/creator if canonicalized, year, language. | Movie/film classes; TMDB maps to the movie property. | Not edition-aware in the bridge worker; returns work identity. |
| TV | `tmdb_id`, `imdb_id`, `tvdb_id`, Apple TV show/episode IDs when present. | Show name or series as title, author/creator if canonicalized, year, language. | TV-series classes; TMDB maps to the TV-series property. | Not edition-aware in the bridge worker; resolves series/show identity. |
| Comics | `comic_vine_id`, `gcd_id`, `isbn` when present. | Series plus title, series title, writer/author/illustrator fallback, year, language. | Comic issue when a series title is present; otherwise comic series. | Not edition-aware in the bridge worker; resolves issue or series identity depending on hints. |

When a bridge ID resolves, Wikidata supplies canonical identity, relationship facts, people, series/franchise data, and additional bridge identifiers. If retail succeeded but Wikidata cannot resolve a QID, the item keeps its retail metadata and is marked as a missing-QID outcome rather than being silently changed. It can still receive a Read, Watch, or Listen shelf from provider/local grouping metadata; it only becomes a top-level Collections rollup when trusted shared relationships connect multiple shelves.

---

## Controlling provider order

For each media type, you can control which provider's data is preferred when multiple providers return conflicting information. This is the **provider priority** order.

1. Go to **Settings -> Providers**.
2. Select **Retail Lookup**.
3. Select the media type you want to adjust (Books, Movies, TV, and so on).
4. You will see the providers listed in their current priority order.
5. Drag providers up or down to change the order. Providers at the top are preferred over providers further down.

The provider priority affects how the Priority Cascade resolves conflicts. When two providers disagree about a title or description, the one higher in the list wins - unless a user lock or Wikidata data overrides it.

The **Canonical Identity** stage is intentionally different. It shows Wikidata identity, bridge, and relationship settings so you can understand what canonical data is being tracked; it does not assign providers to media types. The **Enrichment & Artwork** stage shows focused enrichment providers such as Fanart.tv, LRCLIB, and OpenSubtitles.

---

## Language strategy per provider

Providers differ in what languages they support. Each provider has a **language strategy** that controls which language the Engine queries it in.

| Strategy | What it means |
|---|---|
| **Source** | Always query this provider in English, regardless of your language settings. Use this for providers whose data is English-only or not localized. |
| **Localized** | Query this provider in your metadata language setting. Use this for providers with strong international content (TMDB, Apple API). |
| **Both** | Query in your metadata language first; if the result is empty, retry in English and merge the results. Wikidata uses this by default. |

To change the language strategy for a provider:

1. Go to **Settings -> Providers**.
2. Click the provider you want to configure.
3. Find the **Language Strategy** dropdown in the provider's settings panel.
4. Select the strategy you want and click **Save**.

---

## How cover art is handled

Each provider that supports cover art downloads images into the managed asset store under `.data/assets/...` and records them in the database, usually through `entity_assets`. You never need to re-download them. Images beside media files are optional export mirrors only when storage policy enables them.

When multiple providers supply cover art for the same title, the Engine also checks the artwork visually against the cover already embedded in the file. This comparison helps identify the best match and can improve the Engine's confidence in its identification - not just the quality of the image.

In the Review Queue, you can see all the cover art options gathered for any item. Open the item's detail drawer and look at the **Assets** section. You can set a preferred image, upload your own, or keep the one the Engine selected automatically. Any image you upload is protected and will never be overwritten by an automatic refresh.

---

## The 30-day refresh cycle

Providers update their data over time. New editions are added, descriptions are improved, cover art is refreshed. The Engine automatically re-queries providers for all items in your library every 30 days to pick up these improvements.

You can also trigger a manual refresh at any time. In the Review Queue, select one or more items and click **Sync Now** in the floating action bar. This immediately re-runs both enrichment stages for the selected items.

## Related

- [Providers Reference](../reference/providers.md)
- [Configuration Reference](../reference/configuration.md)
- [How Two-Stage Enrichment Works](../explanation/how-hydration-works.md)
