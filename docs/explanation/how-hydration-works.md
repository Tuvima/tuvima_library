---
title: "How Two-Stage Enrichment Works"
summary: "Learn how retail providers and Wikidata combine to enrich media after ingestion."
audience: "user"
category: "explanation"
product_area: "providers"
tags:
  - "hydration"
  - "providers"
  - "wikidata"
---

# How Two-Stage Enrichment Works

Once a file has been ingested and identified, the real enrichment begins. The Engine fetches cover art, descriptions, ratings, author biographies, franchise relationships, and dozens of structured properties from external sources. This process is called **hydration** â€” the Engine takes the dry skeleton of a matched file and fills it with living detail.

Hydration happens in two stages, and understanding why it's split that way explains a lot about how the Engine makes decisions.

---

## Why Two Stages?

No single source has everything.

Retail providers â€” Apple API, Google Books, TMDB, Open Library â€” have excellent cover art, reader ratings, plot descriptions, and narrator credits. Their images are often professionally produced. Their descriptions are written for human readers. But their structured metadata (the facts: canonical author, series position, year of first publication, genre classification) can be inconsistent. They use house-style name formatting, regional publication dates, and categories that vary between stores.

**Wikidata** is the opposite. It has authoritative structured data, community-maintained and cited, covering title, author, genre, series membership, franchise relationships, cast, crew, and more. But Wikidata doesn't have cover art. It doesn't have reader ratings. And its coverage isn't universal â€” very recent or obscure works might have minimal data.

The two-stage design gets the best of both:

- **Stage 1** (Retail): get the rich presentation data *and* the bridge IDs that make Stage 2 precise
- **Stage 2** (Wikidata): use those bridge IDs to fetch authoritative structured data with confidence

---

## Stage 1: Retail Identification

Stage 1 runs retail providers according to a strategy that varies by media type. The strategy and ranked provider list are both configured in `config/pipelines.json`. Three strategies are used:

- **Waterfall** (first match wins) — used for Movies, TV, and Comics. Providers are tried in rank order until a confident match is found. Once one provider returns a good match, the rest are skipped.
- **Cascade** (all providers run, claims merge) — used for Books and Podcasts. Every configured provider runs independently, and the resulting metadata claims are merged. This allows a book to pick up cover art from Google Books and structured data from Open Library in a single pass.
- **Sequential** (chained, each feeds the next via bridge IDs) — used for Audiobooks and Music. Provider A runs first and passes its bridge IDs to Provider B, which uses them for a more precise lookup than a text search could achieve.

Each provider returns:

**Cover art** â€” the file's embedded artwork is compared to the provider's returned image using perceptual hashing (pHash). Similar images score higher as match evidence. This prevents the Engine from accepting a confident title match that returns an obviously wrong cover.

**Description and plot summary** â€” used both for display and as evidence in candidate ranking. A description mentioning the same characters as the file's embedded metadata is a stronger match signal.

**Ratings** â€” reader/critic scores from the provider, stored as Claim data.

**Bridge IDs** â€” this is the key output. Bridge IDs are the identifiers that cross-reference items between databases:
- ISBN-13 / ISBN-10 (books)
- ASIN (Amazon, primarily audiobooks)
- TMDB ID (films and TV)
- MusicBrainz Release ID (music)
- Apple Music ID (music, for Stage 2 Wikidata lookup via P4857)

These IDs are stored and carried forward to Stage 2.

---

## Stage 2: Wikidata Bridge

Stage 2 uses the bridge IDs collected in Stage 1 to find the corresponding Wikidata entry with much higher precision than a text search could provide.

The lookup sequence:
1. Try ISBN â†’ find the specific Wikidata edition item
2. From the edition item, walk up to the work item (P629 "edition of")
3. If no ISBN match, try TMDB ID, ASIN, or MusicBrainz ID
4. If no bridge ID matches, fall back to title + author text search

Text search results are filtered by P31 (instance of) type allow-lists — configured in `instance_of_classes` in `config/providers/wikidata_reconciliation.json` — to prevent false matches (e.g., a movie matching a novel with the same title).

ISBN-first lookup is significantly more precise than text search. "Dune" as a title search returns many candidates. The ISBN for a specific edition points to exactly one item.

Once the work QID is found, the Data Extension API fetches 50+ properties in a single batch call. These include:

**Core bibliographic properties:**
- P50 (author), P57 (director), P58 (screenwriter), P86 (composer), P110 (illustrator)
- P577 (publication date â€” first publication, not reprint)
- P136 (genre), P921 (main subject)
- P179 (part of series), P8345 (media franchise)
- P1680 (subtitle)

**Cast and crew:**
- P161 (cast member, capped at 20 to avoid extremely large casts)
- P725 (voice actor)

**Identifiers for cross-linking:**
- P212 (ISBN-13), P957 (ISBN-10)
- P4947 (TMDB ID), P1260 (ASIN)
- P349 (NDL identifier), P648 (Open Library ID)

For books, audiobooks, and music, when a bridge ID resolves to an edition item rather than a work item, the Engine uses **edition pivot rules** (configured in the `edition_pivot` section of `wikidata_reconciliation.json`) to walk from the edition to its parent work. Audiobooks prefer the edition (P747 edition discovery); books and music resolve to the work.

**Wikipedia descriptions** are also fetched via `GetWikipediaSummariesAsync` â€” the formatted introductory text from Wikipedia, which is rich narrative context the AI's Description Intelligence service can work with.

---

## Bilingual Handling

When a file's detected language differs from your configured metadata language, Stage 2 does something smart: it searches Wikidata in *both* languages and deduplicates the results by QID.

This handles the common case where you have a foreign-language file and want the English metadata (or vice versa). The search finds candidates in both languages, compares them by QID to avoid duplicates, and the Priority Cascade resolves which language's values appear in the display.

When a QID is confirmed and the file's language differs from the metadata language, the Engine also fetches the item's label in the file's original language and stores it as `original_title`. In the Vault, you'll see the display-language title prominently and the original-language title as a smaller subtitle. Both are indexed for search.

---

## Standalone Person Reconciliation

After Stage 2, there's a third pass that runs automatically: standalone person reconciliation.

Any person name found in the metadata â€” from file tags, structured properties, or AI extraction â€” that doesn't yet have a companion Wikidata QID gets searched independently. The search:

1. Queries Wikidata using the name string
2. Filters results to Q5 (human) â€” no accidentally matching fictional characters or organizations
3. Scores candidates by occupation match (is this person an author? a film director?) and notable work alignment
4. Auto-accepts matches at â‰¥0.80 confidence (0.75 for AI-extracted names, which are less reliable)

Person freshness check: if a person has been enriched within the last 30 days, they skip re-fetch. For stale persons, the Engine checks `last_revision_id` first â€” if Wikidata hasn't changed since the last fetch, a full property re-fetch is skipped. This keeps person data current without hammering the Wikidata API.

Person directories under `.data/images/people/{QID}/` are created lazily â€” only when a headshot image actually exists. Empty directories are never created just to reserve a space.

---

## Two-Pass Architecture

Hydration doesn't make you wait for everything before showing results. It uses two passes with different goals:

**Pass 1 (Quick, Immediate)**
Gets the file onto the Dashboard fast. Basic metadata, cover art, title, author. This happens as soon as the file is matched and staged. You'll see the item in your library within seconds of it being identified, even if the deep enrichment hasn't run yet.

**Pass 2 (Universe, Background/Scheduled)**
Runs in the background. Deep enrichment: franchise relationships, full cast data, Universe Graph connections, character extraction, vibe tags, TL;DR generation. This might take minutes or hours depending on your hardware tier and how many items are in the queue.

The distinction matters when you're adding a large collection. You don't have to wait for the entire enrichment pipeline before you can start browsing. Items appear progressively as their quick enrichment completes, and the detail deepens over time.

---

## Provider Response Caching

The Engine caches provider responses within their TTL (time-to-live). If three items in your library all link to the same Wikidata work QID, the properties are fetched once and reused â€” not fetched three times. This matters especially for series, where every entry in a 15-volume set would otherwise trigger separate API calls to fetch franchise and series data that's identical for all of them.

---

## The 30-Day Refresh Cycle

Hydration isn't a one-time event. Every 30 days, enrichment re-runs for all items. This catches:

- Updated Wikidata properties (new cast members added, series position corrected)
- New provider data (cover art updated, new edition information)
- Person data that was missing and has since been added to Wikidata

**Group-level refresh** applies to TV series and music albums. When any episode in a TV series triggers a stale refresh, all sibling episodes in the same series are included in the same batch. This keeps series data consistent â€” if a show's season count is updated in Wikidata, all episodes get the updated data in the same refresh cycle rather than staggering out over days as individual episodes hit their 30-day mark.

---

For technical details about provider configuration, waterfall priority configuration, the Wikidata property fetch list, caching TTLs, and the full hydration pipeline implementation â€” see the [architecture deep-dive](../architecture/hydration-and-providers.md).

## Related

- [How the Entire Pipeline Works](how-the-pipeline-works.md) — end-to-end pipeline overview
- [Hydration Pipeline, Provider Architecture and Enrichment Strategy](../architecture/hydration-and-providers.md)
- [Providers Reference](../reference/providers.md)
- [How to Configure Metadata Providers](../guides/configuring-providers.md)
