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

Providers work in two stages:

1. **Retail providers** (Stage 1) â€” run first. These gather practical information: cover art, descriptions, ratings, and identifiers. The Engine uses this data both to enrich your library and to improve its confidence in identifying what the file is.

2. **Wikidata** (Stage 2) â€” runs second, using identifiers gathered in Stage 1. Wikidata is the authority for canonical structured data: the author's full name, the official series name, genre classifications, director credits, and so on. Wikidata is always free to use and requires no key.

---

## Providers that work out of the box

These providers require no account, no sign-up, and no configuration. They are active as soon as you install Tuvima Library.

| Provider | What it supplies |
|---|---|
| **Wikidata** | Canonical identity, structured metadata, people, series, genre |
| **Wikipedia** | Plain-language descriptions |
| **Apple API** | Cover art, descriptions, ratings (books, audiobooks, music) |
| **Open Library** | Book metadata, ISBN data |
| **Google Books** | Book metadata, cover art, descriptions |
| **MusicBrainz** | Music metadata, release data, artist information |

These providers are enabled by default. You don't need to do anything to start using them.

---

## Providers that require an API key

Some providers require you to create a free account and obtain an API key before they can be used. The key lets the provider's service know the request is coming from your installation.

### TMDB (The Movie Database)

TMDB supplies cover art, descriptions, cast and crew, ratings, and backdrops for movies and TV.

1. Go to `https://www.themoviedb.org/settings/api` and create a free account.
2. Request an API key (choose "Developer" use type).
3. Copy the key.
4. In the Dashboard, go to **Settings â†’ Providers â†’ Connections**.
5. Find TMDB in the list and paste your key into the API Key field.
6. Click **Save**.

### Comic Vine

Comic Vine supplies metadata for comics â€” issue numbers, story arcs, publishers, and character information.

1. Go to `https://comicvine.gamespot.com/api/` and create a free account.
2. Click **Get API Key**.
3. Copy the key.
4. In the Dashboard, go to **Settings â†’ Providers â†’ Connections**.
5. Find Comic Vine and paste your key.
6. Click **Save**.

---

## Controlling provider order

For each media type, you can control which provider's data is preferred when multiple providers return conflicting information. This is the **provider priority** order.

1. Go to **Settings â†’ Providers**.
2. Select the media type you want to adjust (Books, Movies, TV, and so on).
3. You will see the providers listed in their current priority order.
4. Drag providers up or down to change the order. Providers at the top are preferred over providers further down.

The provider priority affects how the Priority Cascade resolves conflicts. When two providers disagree about a title or description, the one higher in the list wins â€” unless a user lock or Wikidata data overrides it.

---

## Language strategy per provider

Providers differ in what languages they support. Each provider has a **language strategy** that controls which language the Engine queries it in.

| Strategy | What it means |
|---|---|
| **Source** | Always query this provider in English, regardless of your language settings. Use this for providers whose data is English-only (Open Library, Google Books, MusicBrainz). |
| **Localized** | Query this provider in your metadata language setting. Use this for providers with strong international content (TMDB, Apple API). |
| **Both** | Query in your metadata language first; if the result is empty, retry in English and merge the results. Wikidata uses this by default. |

To change the language strategy for a provider:

1. Go to **Settings â†’ Providers**.
2. Click the provider you want to configure.
3. Find the **Language Strategy** dropdown in the provider's settings panel.
4. Select the strategy you want and click **Save**.

---

## How cover art is handled

Each provider that supports cover art will download images and store them locally in your library's data directory. You never need to re-download them.

When multiple providers supply cover art for the same title, the Engine also checks the artwork visually against the cover already embedded in the file. This comparison helps identify the best match and can improve the Engine's confidence in its identification â€” not just the quality of the image.

In the Library Vault, you can see all the cover art options gathered for any item. Open the item's detail drawer and look at the **Assets** section. You can set a preferred image, upload your own, or keep the one the Engine selected automatically. Any image you upload is protected and will never be overwritten by an automatic refresh.

---

## The 30-day refresh cycle

Providers update their data over time. New editions are added, descriptions are improved, cover art is refreshed. The Engine automatically re-queries providers for all items in your library every 30 days to pick up these improvements.

You can also trigger a manual refresh at any time. In the Library Vault, select one or more items and click **Sync Now** in the floating action bar. This immediately re-runs both enrichment stages for the selected items.

## Related

- [Providers Reference](../reference/providers.md)
- [Configuration Reference](../reference/configuration.md)
- [How Two-Stage Enrichment Works](../explanation/how-hydration-works.md)
