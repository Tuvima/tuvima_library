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
> A complete reference of every retail provider in the Tuvima Library pipeline - what they accept as search parameters, what they return, and how their output feeds into Stage 4 (Wikidata) resolution. This is the authoritative source for provider capabilities.

---

## How Providers Fit Into the Pipeline

The provider portion of ingestion maps to the numbered stages shown on the Ingestion page:

1. **Stage 3 (Retail metadata & primary artwork):** Retail providers search for the media item using file metadata. They return primary cover/poster evidence, descriptions, ratings, and - critically - **bridge IDs** (ISBN, TMDB ID, etc.) that uniquely identify the item on external platforms.

2. **Stage 4 (Wikidata lookup):** The Wikidata Reconciliation adapter uses those bridge IDs to resolve the item's Wikidata QID. Each bridge ID maps to a Wikidata property code (for example, ISBN-13 maps to P212). Stage 4 is strict-gated behind Stage 3: no safe retail match means no automatic Wikidata attempt. A Stage 4 request also needs at least one real bridge ID; title and creator hints are sent as ranking context, not as a broad title-only fallback.

3. **Stage 8 (Deep artwork):** Rich artwork providers such as Fanart.tv run later, after bridge IDs or QIDs are available. They do not provide the first cover/poster pass.

Retail providers are a **rich data source for matching** - descriptions, narrator data, ratings, and cover art similarity are all used to rank candidates against file metadata. **Wikidata is the authority** for final canonical values (title, author, year, genre, series).

---

## Provider Summary

| Provider | Media Types | Auth Required | Rate Limit | Language Strategy | Status |
|---|---|---|---|---|---|
| Apple API | Books, Audiobooks, Music | None | 500ms throttle | Localized (user language) | Active |
| TMDB | Movies, TV | API key query parameter | 500ms throttle, max 1 concurrent | Localized (user language) | Active (requires key) |
| MusicBrainz | Music | None | 1100ms, max 1 concurrent | Source (English only) | Disabled (config kept) |
| Comic Vine | Comics | API key | 500ms, max 1 concurrent | Source (English only) | Active (requires key) |
| Open Library | Books | None | 500ms | Source (English only) | Disabled (config kept) |
| Fanart.tv | Movies, TV, Music | API key | Configured provider throttle | ID lookup | Stage 8 deep artwork only |
| LRCLIB | Music | None | Configured provider throttle | Source | Text-track provider |
| OpenSubtitles | Movies, TV | API key | Configured provider throttle | Source | Text-track provider, disabled by default |

---

## Query Parameters - What We Send

Provider search is defined in `config/providers/*.json`, with special grouped worker paths for Music and TV. Most providers accept one primary search term, but Apple and Open Library title searches include creator text through `query_template` when available. The returned candidates are still validated afterward by `RetailMatchScoringService`.

API credentials are config-file data. Base provider definitions live in `config/providers/*.json`; optional secret overlays live in `config/secrets/{provider}.json` and are applied by `ConfigurationDirectoryLoader` on `LoadProvider` and `LoadAllProviders`. A blank `http_client.api_key` in the base provider file does not mean the effective runtime key is missing if a matching secrets file exists.

### Active Retail Lookup Matrix

| Media type | Provider | Search or lookup strategy | Lookup inputs | Scoring inputs |
|---|---|---|---|---|
| Books | Apple API | ISBN lookup, Apple Books ID lookup, ebook search | `isbn`, `apple_books_id`, then `title` plus `author` in the Apple search term. | Title, author, year/date, format, description/publisher/page-count cross-checks, cover similarity. |
| Audiobooks | Apple API | Apple Books ID lookup, audiobook search | `apple_books_id`, then `title` plus `author` in the Apple search term. | Title, author, narrator-in-description, year/date, duration, format, cover similarity. |
| Music | Apple API | Grouped track-first search, album search, collection lookup | `title` plus `artist` for track search; `artist` plus `album` for album search; `apple_music_collection_id` for collection lookup. | Track title, artist/composer/author, album, year/date, track number, duration, format, cover similarity. |
| Movies | TMDB | Movie search | `title`; `year` when available; `api_key`; locale. | Title, year, local director/writer/author evidence, format, genre/description cross-checks, poster similarity. |
| TV | TMDB | Grouped show search, season episode lookup | `show_name` or `series`; year from any episode in the group; `season_number`; `api_key`; locale. | Episode title, show/series, season number, episode number, year, format, poster/still similarity. |
| Comics | Comic Vine | Issue search, volume search | `title` for issue search; `series` for volume search; `api_key`. | Title, series, issue number or series position, writer/author/illustrator, year, format, cover similarity. |

### Per-Provider Search Strategies

#### Apple API

| Strategy | Priority | Required Fields | URL Pattern | Media Types |
|---|---|---|---|---|
| ISBN Lookup | 0 (highest) | `isbn` | `/lookup?isbn={isbn}` | Books |
| Apple ID Lookup | 1 | `apple_books_id` | `/lookup?id={apple_books_id}&country={country}&lang={lang}_{country}` | Books, Audiobooks |
| Ebook Search | 2 | `title` | `/search?term={title} {author}&entity=ebook&limit={limit}&country={country}&lang={lang}_{country}` | Books |
| Audiobook Search | 2 | `title` | `/search?term={title} {author}&entity=audiobook&limit={limit}&country={country}&lang={lang}_{country}` | Audiobooks |
| Music Track Search | 2 | `title` | `/search?term={title} {artist}&entity=musicTrack&limit={limit}&country={country}&lang={lang}_{country}` | Music |
| Music Album Search | 3 | `title` | `/search?term={artist} {title}&entity=album&limit={limit}&country={country}&lang={lang}_{country}` | Music |
| Music Album Lookup | 4 | `apple_music_collection_id` | `/lookup?id={apple_music_collection_id}&entity=song&country={country}&lang={lang}_{country}` | Music |

**Notes:** ISBN lookup is exact match (1 result). Book and audiobook search returns up to 25 results and fetches the top 5. Music has a grouped worker path that first searches by track to discover the Apple collection ID, then fetches the album so sibling tracks can share one provider lookup where possible. Cover art URL transforms remove Apple's size constraints for higher resolution.

#### TMDB

| Strategy | Priority | Required Fields | URL Pattern | Media Types |
|---|---|---|---|---|
| Movie Search | 1 | `title` | `/search/movie?query={title}&year={year}&include_adult=false&language={lang}-{country}&page=1` | Movies |
| TV Search | 1 | `title` | `/search/tv?query={title}&first_air_date_year={year}&include_adult=false&language={lang}-{country}&page=1` | TV |
| TV Season Episodes | 2 | `tmdb_id`, `season_number` | `/tv/{tmdb_id}/season/{season_number}?language={lang}-{country}` | TV |

**Notes:** Year is optional but included when available from file metadata. TV jobs are grouped by show and season: the worker searches the show, fetches show details, then fetches the season episode list and distributes episode-level claims to queued files. Returns poster paths that are expanded to `https://image.tmdb.org/t/p/w500{path}`.

#### MusicBrainz (Disabled)

| Strategy | Priority | Required Fields | URL Pattern | Media Types |
|---|---|---|---|---|
| Release Search | 1 | `title` | `/release?query={title}&fmt=json&limit={limit}` | Music |
| Recording Search | 2 | `title` | `/recording?query={title}&fmt=json&limit={limit}` | Music |

**Notes:** The config is retained but disabled by default, so MusicBrainz is not an active Stage 3 provider in the normal runtime path. If re-enabled, its configured searches combine title and artist where artist is available, while album and year remain post-search ranking signals. Embedded MusicBrainz tags from local files can still exist as local metadata/bridge evidence.

#### Comic Vine

| Strategy | Priority | Required Fields | URL Pattern | Media Types |
|---|---|---|---|---|
| Issue Search | 1 | `title` | `/search/?query={title}&resources=issue&limit={limit}` | Comics |
| Volume Search | 2 | `series` | `/search/?query={series}&resources=volume&limit={limit}` | Comics |

**Notes:** Comic Vine supplies comic issue and volume metadata, including cover art and Comic Vine bridge identifiers. Series and issue hints are used for search and post-search ranking.

#### Open Library (Disabled)

| Strategy | Priority | Required Fields | URL Pattern | Media Types |
|---|---|---|---|---|
| ISBN Search | 1 | `isbn` | `/search.json?isbn={isbn}&limit=3&fields=...` | Books |
| Title Search | 2 | `title` | `/search.json?q={title} {author}&limit={limit}&fields=...` | Books |

**Notes:** Title search combines `{title} {author}` when author is available. ISBN search returns up to 3 results. Returns first_sentence as description (lower quality than dedicated description APIs). Currently disabled but config preserved for future use.

#### Fanart.tv (Stage 8 Only)

Fanart.tv is not an identity provider. It runs after identity is established and uses bridge IDs to fetch additional artwork such as backgrounds, logos, banners, thumbnails, clear art, disc art, and square art.

#### LRCLIB and OpenSubtitles

LRCLIB and OpenSubtitles provide lyrics and subtitle/text-track data. They do not decide identity, do not unlock Wikidata resolution, and do not participate in retail candidate scoring.

---

## Response Fields - What We Extract

Each provider's config defines a `field_mappings` array that maps JSON response paths to claim keys. These become `ProviderClaim` objects (key, value, confidence) stored in `metadata_claims`.

### Extraction Summary

| Provider | Active media | Main claims extracted | Bridge claims extracted |
|---|---|---|---|
| **Apple API** | Books, Audiobooks, Music | Title, author/artist, year, cover, description, genre, rating for books, album, track/disc counts, track number, duration. Claim confidence is usually 0.70 to 0.90 depending on field and media type. | `apple_books_id`, `apple_music_id`, `apple_music_collection_id`, `apple_artist_id`. |
| **TMDB** | Movies, TV | Title, year, cover, description, short description, rating, original language, genre, network. Claim confidence is usually 0.80 to 0.90. | `tmdb_id`. |
| **Comic Vine** | Comics | Title, series, issue number, description, cover, year, series position. Claim confidence is usually 0.70 to 0.95. | `comic_vine_id`. |
| **MusicBrainz** | Disabled by default | Title, artist/author, album, year, track count, MusicBrainz IDs, cover URL if re-enabled. | MusicBrainz artist/work/release/recording/release-group IDs when present. |
| **Open Library** | Disabled by default | Title, author, year, cover, description, publisher, language, ISBN if re-enabled. | `isbn` / Open Library identifiers when present. |

Numbers in provider JSON represent confidence values assigned to extracted claims when the provider is enabled.

### Value Transforms

The `ValueTransformCatalog` applies transformations during extraction:

| Transform | Purpose | Used By |
|---|---|---|
| `regex_replace` | Strip image size suffixes from cover URLs | Apple API |
| `strip_html` | Clean HTML tags from descriptions | Apple API, Comic Vine, Open Library |
| `to_string` | Convert numeric values such as IDs and ratings to strings | Apple API, TMDB, Comic Vine |
| `url_template` | Construct full image URLs from partial paths | TMDB, MusicBrainz, Open Library |
| `first_n_chars(4)` | Extract 4-digit year from date strings | Apple API, TMDB, MusicBrainz, Comic Vine |
| `prefer_isbn13` | Select ISBN-13 from array of ISBN formats | Open Library |
| `array_join` | Join array elements into comma-separated strings | Apple API, TMDB, MusicBrainz |

---

## Bridge IDs - What Feeds Stage 4

Bridge IDs are external platform identifiers that the Wikidata Reconciliation adapter uses to resolve the item's Wikidata QID after Stage 3 has produced a safe retail match. Each bridge ID maps to a Wikidata property code.

### Bridge IDs by Provider

| Provider | Bridge ID | Claim Key | Confidence | Wikidata Property |
|---|---|---|---|---|
| **Apple API** | Apple Books ID | `apple_books_id` | 0.95 | P6395 |
| **Apple API** | Apple Music Track ID | `apple_music_id` | 0.95 | P10110 |
| **Apple API** | Apple Music Collection ID | `apple_music_collection_id` | 0.95 | P2281 |
| **Apple API** | Apple Artist ID | `apple_artist_id` | 0.90 | P2850 |
| **TMDB** | TMDB ID | `tmdb_id` | 1.0 | P4947 (movies) / P4983 (TV) |
| **Comic Vine** | Comic Vine ID | `comic_vine_id` | 0.95 | P5905 |
| **MusicBrainz** | MusicBrainz IDs | `musicbrainz_id`, `musicbrainz_recording_id`, `musicbrainz_release_group_id` | provider-dependent | P434/P435/P436/P5813/P4404 depending on ID type |
| **Open Library / file evidence** | ISBN and Open Library ID | `isbn`, `isbn_13`, `isbn_10`, `open_library_id` | provider-dependent | P212/P957/P648 |

### Stage 4 Resolution Flow Per Bridge ID

The Reconciliation adapter is now a thin orchestrator over `Tuvima.Wikidata` v3.0's `BridgeResolutionService`.

1. **Bridge request build:** The adapter converts each `WikidataResolveRequest` into a `BridgeResolutionRequest` with bridge IDs, media kind, title/creator/year/series hints, language, custom P-code mappings, and rollup preference.
2. **Direct lookup:** The package groups `(propertyId, normalizedValue)` lookups so duplicate ISBN/TMDB/Apple/MusicBrainz/ComicVine IDs share one Wikidata query.
3. **Edition awareness:** The package walks P629 for edition/release-to-work rollups and can return both the resolved entity QID and canonical work QID plus the relationship path.
4. **Media-specific bridge mapping:** TMDB, Apple, TVDB, MusicBrainz, OpenLibrary, and ComicVine keys are mapped to official Wikidata properties inside the package; app config only overrides or supplies custom mappings.
5. **Strict bridge gate:** If a request has no real bridge IDs, the adapter does not build a Stage 4 bridge request. Title-only automatic requests are skipped.
6. **Claim and diagnostics follow-up:** After every successful resolution, the adapter calls `ExtendAsync` over the known bridge P-codes to populate `WikidataResolveResult.Claims` and `CollectedBridgeIds`, and it also carries `BridgeDiagnostics`, ranked candidates, and rollup details from the package result.

Manual Wikidata searches from the editor and automated bridge resolution both flow through `ReconciliationAdapter` before calling `Tuvima.Wikidata`, but manual user searches are not the same as automatic Stage 4 fallback. Exact QID searches such as `Q155653` are passed through as identity lookups, so the package fetches the entity directly and still applies configured media type constraints.

### Wikidata Lookup Matrix

The bridge worker sends these fields to Wikidata after Stage 3 has produced a retail match and at least one bridge ID.

| Media type | Bridge IDs | Hints | Media kind / filter |
|---|---|---|---|
| Books | `isbn`, `isbn_13`, `isbn_10`, `asin`, `apple_books_id`, `open_library_id`, `goodreads_id` | Title, author, year, language | Book/literary work, edition-aware |
| Audiobooks | `apple_books_id`, `isbn`, `asin`, `audible_id`, MusicBrainz IDs | Title, author, year, language | Audiobook, edition-aware, prefers edition |
| Music | `apple_music_id`, `apple_music_collection_id`, `apple_artist_id`, MusicBrainz IDs | Album, artist, track title, year, language | Music album when album is known; otherwise music work |
| Movies | `tmdb_id`, `imdb_id`, Apple TV movie IDs | Title, creator if canonicalized, year, language | Movie/film |
| TV | `tmdb_id`, `imdb_id`, `tvdb_id`, Apple TV show/episode IDs | Show name or series, creator if canonicalized, year, language | TV series |
| Comics | `comic_vine_id`, `gcd_id`, `isbn` | Series plus title, series title, writer/author/illustrator, year, language | Comic issue when series is known; otherwise comic series |

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

Embedded cover art from the file is perceptually hashed (aHash - 64-bit fingerprint via 8x8 grayscale resize). When retail provider cover art is downloaded, it's hashed and compared. The Hamming distance produces a similarity score (0.0-1.0) that feeds into the scoring composite as a confidence boost.

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
