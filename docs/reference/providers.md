---
title: "Providers Reference"
summary: "Review the capabilities, rate limits, bridge IDs, and behaviors of the retail providers in the hydration pipeline."
audience: "operator"
category: "reference"
product_area: "providers"
tags:
  - "providers"
  - "retail-identification"
  - "enrichment"
---

# Retail Provider Reference

> **What is this document?**
> A complete reference of every retail provider in the Tuvima Library pipeline — what they accept as search parameters, what they return, and how their output feeds into Stage 2 (Wikidata) resolution. This is the authoritative source for provider capabilities.

---

## How Providers Fit Into the Pipeline

The hydration pipeline runs in two stages:

1. **Stage 1 (Retail Identification):** Retail providers search for the media item using file metadata. They return cover art, descriptions, ratings, and — critically — **bridge IDs** (ISBN, TMDB ID, etc.) that uniquely identify the item on external platforms.

2. **Stage 2 (Wikidata Bridge):** The Wikidata Reconciliation adapter uses those bridge IDs to resolve the item's Wikidata QID. Each bridge ID maps to a Wikidata property code (e.g. ISBN-13 maps to P212). Stage 2 is strict-gated behind Stage 1: no safe Retail match means no automatic Wikidata attempt. When real bridge IDs were tried and still failed, controlled text search can still be used as a last resort.

Retail providers are a **rich data source for matching** — descriptions, narrator data, ratings, and cover art similarity are all used to rank candidates against file metadata. **Wikidata is the authority** for final canonical values (title, author, year, genre, series).

---

## Provider Summary

| Provider | Media Types | Auth Required | Rate Limit | Language Strategy | Status |
|---|---|---|---|---|---|
| Apple API | Books, Audiobooks | None | 500ms throttle | Localized (user language) | Active |
| TMDB | Movies, TV | Bearer token | 250ms, max 2 concurrent | Localized (user language) | Active (requires key) |
| MusicBrainz | Music | None | 1100ms, max 1 concurrent | Source (English only) | Active |
| Metron | Comics | Basic auth | 500ms, max 1 concurrent | Source (English only) | Active (requires key) |
| Open Library | Books | None | 500ms | Source (English only) | Disabled (config kept) |
| Google Books | Books | API key (query param) | 200ms | Localized | Removed |

---

## Query Parameters — What We Send

Most providers only accept **Title** as a free-text search parameter. Author, Director, Narrator, and Artist are **not** sent to the provider API — they are used by the `RetailMatchScoringService` to rank results *after* they come back.

### Per-Provider Search Strategies

#### Apple API

| Strategy | Priority | Required Fields | URL Pattern | Media Types |
|---|---|---|---|---|
| ISBN Lookup | 0 (highest) | `isbn` | `/lookup?isbn={isbn}` | Books |
| Apple ID Lookup | 1 | `apple_books_id` | `/lookup?id={apple_books_id}&country={country}&lang={lang}_{country}` | Books, Audiobooks |
| Ebook Search | 2 | `title` | `/search?term={title}&entity=ebook&limit={limit}&country={country}&lang={lang}_{country}` | Books |
| Audiobook Search | 2 | `title` | `/search?term={title}&entity=audiobook&limit={limit}&country={country}&lang={lang}_{country}` | Audiobooks |

**Notes:** ISBN lookup is exact match (1 result). Title search returns up to 25 results, fetches top 5. Author is not a query parameter — used for post-search ranking only. Cover art URL requires regex transform to remove Apple's size constraints for full resolution.

#### TMDB

| Strategy | Priority | Required Fields | URL Pattern | Media Types |
|---|---|---|---|---|
| Movie Search | 1 | `title` | `/search/movie?query={title}&year={year}&include_adult=false&language={lang}-{country}&page=1` | Movies |
| TV Search | 1 | `title` | `/search/tv?query={title}&first_air_date_year={year}&include_adult=false&language={lang}-{country}&page=1` | TV |

**Notes:** Year is optional but included when available from file metadata. Director is not a query parameter — used for post-search ranking only. Returns poster and backdrop image paths (prepend `https://image.tmdb.org/t/p/w500` or `w1280`).

#### MusicBrainz

| Strategy | Priority | Required Fields | URL Pattern | Media Types |
|---|---|---|---|---|
| Release Search | 1 | `title` | `/release?query={title}&fmt=json&limit={limit}` | Music |
| Recording Search | 2 | `title` | `/recording?query={title}&fmt=json&limit={limit}` | Music |

**Notes:** Title only. Artist, Album, and Year are not query parameters — used for post-search ranking. Cover art from Cover Art Archive (`https://coverartarchive.org/release/{id}/front-250`). Strict rate limit (1100ms between requests, max 1 concurrent) per MusicBrainz policy.

#### Metron

| Strategy | Priority | Required Fields | URL Pattern | Media Types |
|---|---|---|---|---|
| ISBN Lookup | 0 (highest) | `isbn` | `/issue/?isbn={isbn}` | Comics |
| Issue Search | 1 | `title` | `/issue/?series_name={title}&limit={limit}` | Comics |
| Series Search | 2 | `title` | `/series/?name={title}&limit={limit}` | Comics |

**Notes:** ISBN lookup is exact match. Title is used as `series_name` parameter for issue search, `name` for series search. Author and Year are not query parameters. Returns rich comic metadata including publisher, credits, issue number, and series position.

#### Open Library (Disabled)

| Strategy | Priority | Required Fields | URL Pattern | Media Types |
|---|---|---|---|---|
| ISBN Search | 1 | `isbn` | `/search.json?isbn={isbn}&limit=3&fields=...` | Books |
| Title Search | 2 | `title` | `/search.json?q={title} {author}&limit={limit}&fields=...` | Books |

**Notes:** The only provider whose title search also sends `{author}` in the query (combined as `{title} {author}`). ISBN search returns up to 3 results. Returns first_sentence as description (lower quality than dedicated description APIs). 14-day cache TTL. Currently disabled but config preserved for future use.

---

## Response Fields — What We Extract

Each provider's config defines a `field_mappings` array that maps JSON response paths to claim keys. These become `ProviderClaim` objects (key, value, confidence) stored in `metadata_claims`.

### Extraction Summary

| Provider | Title | Author | Year | Cover Art | Description | Rating | Publisher | Series | Genre | Language |
|---|---|---|---|---|---|---|---|---|---|---|
| **Apple API** | -- | -- | -- | 0.85 | 0.85 | 0.70 (Books) | -- | -- | -- | -- |
| **TMDB** | 0.85 | -- | 0.90 | 0.90 (poster+backdrop) | 0.85 | 0.80 | -- | -- | 0.85 | 0.85 |
| **MusicBrainz** | 0.80 | 0.80 (artist-credit) | 0.85 | 0.70 | -- | -- | -- | -- | -- | -- |
| **Metron** | 0.85 | 0.80 (credits) | 0.85 | 0.85 | 0.80 | -- | 0.85 | 0.90 | -- | -- |
| **Open Library** | 0.75 | 0.80 | 0.85 | 0.70 | 0.60 | -- | 0.70 | -- | 0.65 | 0.70 |

Numbers represent confidence values assigned to extracted claims. `--` means the provider does not return that field.

### Value Transforms

The `ValueTransformCatalog` applies transformations during extraction:

| Transform | Purpose | Used By |
|---|---|---|
| `regex_replace` | Strip image size suffixes from cover URLs | Apple API |
| `strip_html` | Clean HTML tags from descriptions | Apple API, Metron, Open Library |
| `to_string` | Convert numeric values (IDs, ratings) to strings | Apple API, TMDB, Metron |
| `url_template` | Construct full image URLs from partial paths | TMDB, MusicBrainz, Open Library |
| `first_n_chars(4)` | Extract 4-digit year from date strings | TMDB, MusicBrainz, Metron |
| `prefer_isbn13` | Select ISBN-13 from array of ISBN formats | Open Library |
| `array_join` | Join array elements into comma-separated string | TMDB (genres), MusicBrainz (artists), Metron (credits), Open Library (genres) |

---

## Bridge IDs — What Feeds Stage 2

Bridge IDs are external platform identifiers that the Wikidata Reconciliation adapter uses to resolve the item's Wikidata QID. Each bridge ID maps to a Wikidata property code.

### Bridge IDs by Provider

| Provider | Bridge ID | Claim Key | Confidence | Wikidata Property |
|---|---|---|---|---|
| **Apple API** | Apple Books ID | `apple_books_id` | 0.95 | P6395 |
| **TMDB** | TMDB ID | `tmdb_id` | 1.0 | P4947 (movies) / P4983 (TV) |
| **MusicBrainz** | MusicBrainz Release ID | `musicbrainz_id` | 1.0 | P436 |
| **MusicBrainz** | ISRC | `isrc` | 0.9 | P1243 |
| **Metron** | Comic Vine ID | `comic_vine_id` | 0.95 | P5905 |
| **Metron** | GCD ID | `gcd_id` | 0.95 | P11957 |
| **Metron** | ISBN | `isbn` | 0.95 | P212 |
| **Open Library** | ISBN | `isbn` | 0.90 | P212 (ISBN-13) / P957 (ISBN-10) |

### Stage 2 Resolution Flow Per Bridge ID

The Reconciliation adapter is now a thin orchestrator over `Tuvima.Wikidata` v3.0's `BridgeResolutionService`.

1. **Bridge request build:** The adapter converts each `WikidataResolveRequest` into a `BridgeResolutionRequest` with bridge IDs, media kind, title/creator/year/series hints, language, custom P-code mappings, and rollup preference.
2. **Direct lookup:** The package groups `(propertyId, normalizedValue)` lookups so duplicate ISBN/TMDB/Apple/MusicBrainz/ComicVine IDs share one Wikidata query.
3. **Edition awareness:** The package walks P629 for edition/release-to-work rollups and can return both the resolved entity QID and canonical work QID plus the relationship path.
4. **Media-specific bridge mapping:** TMDB, Apple, TVDB, MusicBrainz, OpenLibrary, Google Books, and ComicVine keys are mapped to official Wikidata properties inside the package; app config only overrides or supplies custom mappings.
5. **Fallback:** When bridge lookup does not produce a usable candidate and title hints are present, the bridge resolver performs typed text fallback internally. The adapter keeps a small application-level fallback pass for historical parity.
6. **Claim and diagnostics follow-up:** After every successful resolution, the adapter calls `ExtendAsync` over the known bridge P-codes to populate `WikidataResolveResult.Claims` and `CollectedBridgeIds`, and it also carries `BridgeDiagnostics`, ranked candidates, and rollup details from the package result.

### Preferred Bridge ID Order (per media type)

The hydration config specifies which bridge IDs to try first per media type:

| Media Type | Preferred Order |
|---|---|
| Books | `isbn` |
| Audiobooks | `apple_books_id`, `isbn`, `asin` |
| Movies | `tmdb_id`, `imdb_id` |
| TV | `tmdb_id`, `imdb_id` |
| Music | `musicbrainz_id`, `isrc` |
| Comics | `comic_vine_id`, `gcd_id`, `isbn` |

---

## Retail Match Scoring

After a provider returns results, the `RetailMatchScoringService` scores each candidate against file metadata:

### Scoring Weights

| Field | Weight | How Scored |
|---|---|---|
| Title | 0.45 | Fuzzy string similarity (Levenshtein distance) |
| Author/Creator | 0.35 | Fuzzy string similarity |
| Year | 0.10 | Exact = 1.0, off-by-1 = 0.8, off-by-2+ = 0.3 |
| Format | 0.10 | Exact media type match |

### Boosters

| Booster | Condition | Boost |
|---|---|---|
| Cross-field | Narrator found in description, genre overlap | Variable |
| Cover art (strong) | pHash similarity > 0.8 | +0.10 |
| Cover art (moderate) | pHash similarity > 0.6 | +0.05 |

Cover similarity can boost an already plausible candidate, but it cannot rescue weak title or creator evidence on its own.

### Thresholds

| Threshold | Value | Result |
|---|---|---|
| Auto-accept | >= 0.90 | Match accepted automatically and allowed to proceed to Wikidata |
| Ambiguous | 0.65 - <0.90 | Review queue (`RetailMatchAmbiguous`) |
| Failed | < 0.65 or no results | Review queue (`RetailMatchFailed`) |

Additional contradiction gates apply before auto-accept. Weak creator agreement can cap a candidate to review, grouped TV auto-accept requires exact show/season/episode agreement, and grouped music auto-accept requires track-number or duration corroboration.

### Cover Art Matching

Embedded cover art from the file is perceptually hashed (aHash — 64-bit fingerprint via 8x8 grayscale resize). When retail provider cover art is downloaded, it's hashed and compared. The Hamming distance produces a similarity score (0.0-1.0) that feeds into the scoring composite as a confidence boost.

---

## Provider Configuration

All provider configs live in `config/providers/` as individual JSON files. Each is self-contained with connection details, search strategies, response mappings, and bridge ID preferences. Adding a new REST+JSON provider is a zero-code operation: create a config file, restart the Engine.

### Config File Structure

```
{
  "id": "UUID",
  "name": "provider_name",
  "display_name": "Human Name",
  "enabled": true,
  "version": "1.0",
  "domain": "media_domain",
  "media_types": ["Type1", "Type2"],
  "entity_types": ["Work", "MediaAsset"],
  "base_url": "https://api.example.com",
  "weight": 0.8,
  "language_strategy": "localized|source|both",
  "auth": { ... },
  "rate_limit": { "throttle_ms": 500, "max_concurrent": 2 },
  "search_strategies": [ ... ],
  "field_mappings": [ ... ],
  "preferred_bridge_ids": { "MediaType": ["id1", "id2"] }
}
```

See `config/providers/` for complete examples of each provider.

## Related

- [How to Configure Metadata Providers](../guides/configuring-providers.md)
- [How to Add a New Metadata Provider](../guides/adding-a-provider.md)
- [How Two-Stage Enrichment Works](../explanation/how-hydration-works.md)

