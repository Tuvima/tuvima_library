---
title: "How the Entire Pipeline Works"
summary: "Follow a file's complete journey from detection through enrichment, scoring, and organization — understanding why each stage exists."
audience: "user"
category: "explanation"
product_area: "pipeline"
tags:
  - "pipeline"
  - "ingestion"
  - "hydration"
  - "scoring"
  - "organization"
---

# How the Entire Pipeline Works

When you copy a file into a watched folder, Tuvima Library doesn't just log it in a list. That file goes on a journey — through file verification, metadata extraction, retail matching, Wikidata enrichment, confidence scoring, and finally placement into your organized library. Each stage builds on the one before it, and every design decision has a reason behind it.

This page tells the full story from start to finish. It deliberately avoids duplicating the detail of the three companion explanation pages — instead, it shows how all the pieces connect and *why* the pipeline is structured the way it is.

---

## The Big Picture

Here is every step a file takes, in order:

```
NEW FILE APPEARS IN WATCHED FOLDER
          │
          ▼
    ┌─────────────┐
    │   SETTLE    │  Wait for the file to finish copying. Lock-check the handle.
    └──────┬──────┘
           │
           ▼
    ┌─────────────┐
    │ FINGERPRINT │  SHA-256 hash. Permanent identity. Duplicate detection.
    └──────┬──────┘
           │
           ▼
    ┌─────────────┐
    │    SCAN     │  Processor reads embedded metadata → emits Claims.
    └──────┬──────┘
           │
           ▼
    ┌─────────────┐
    │   CLASSIFY  │  AI MediaTypeAdvisor resolves ambiguous formats (MP3/MP4).
    └──────┬──────┘
           │
           ▼
    ┌──────────────────┐
    │  STAGE 1: RETAIL │  Ranked providers match file → cover art, bridge IDs,
    │  IDENTIFICATION  │  descriptions, ratings. Confidence gate: 0.50 / 0.85.
    └──────┬───────────┘
           │ (bridge IDs pass forward)
           ▼
    ┌──────────────────┐
    │  STAGE 2:        │  Bridge IDs → Wikidata QID → canonical properties,
    │  WIKIDATA BRIDGE │  series, franchise, cast, crew, Wikipedia description.
    └──────┬───────────┘
           │
           ▼
    ┌─────────────────┐
    │ PRIORITY CASCADE │  Four tiers resolve conflicts. User lock > field priority
    │                  │  > Wikidata authority > highest confidence.
    └──────┬──────────┘
           │
           ▼
    ┌──────────────────────┐
    │ ORGANIZATION & HUBS  │  ≥0.85 confidence → promoted to organized library.
    │                      │  Hub assignment from series / franchise QIDs.
    └──────────────────────┘
           │
           ▼
    ITEM VISIBLE IN VAULT & DASHBOARD
```

The sections below walk through each phase in detail, focusing on the reasoning — not just the mechanics.

---

## Phase 1 — Ingestion

> **Full detail:** [How File Ingestion Works](how-ingestion-works.md)

The first phase establishes one thing above all else: **trust**. A file doesn't earn its place in the library until the Engine is confident it knows what the file is.

### File Detection and Settling

The Engine watches your configured folders. The moment a file appears, it's noticed — but not immediately acted on. Large files can take minutes to finish copying. Starting to scan a half-written 50 GB video file would produce garbage. So the Engine waits for the file to stop growing, lets a settle period expire, and confirms the file handle is fully released before doing anything.

This patience is cheap to provide and prevents a whole class of problems.

### Fingerprinting

Every file gets a SHA-256 hash computed from its contents. This hash is the file's permanent identity — independent of filename, folder location, or any metadata it might carry.

The fingerprint does two things. First, it enables **resilient tracking**: if you rename or move the file after it has been staged, the Engine recognizes it on the next scan. Second, it enables **deduplication**: if the hash already exists in the data store, the file is a second copy of something already known. Rather than creating a duplicate entry, the Engine creates a new Edition under the existing Work. Your 1080p and 4K copies of the same film coexist as separate Editions, not as two separate items.

### Scanning

A processor reads the file's embedded metadata and emits **Claims** — each Claim being a (source, value, confidence) triple. The processor is selected by magic bytes (the first few bytes of the file itself), not by file extension, because extensions can be wrong.

A title extracted from a well-structured EPUB gets a higher confidence than one guessed from a messy filename. An ISBN extracted from a file tag gets a higher confidence than a title-only match. These confidence differences matter downstream when the Priority Cascade resolves disagreements between sources.

### Classifying Ambiguous Formats

Some formats are genuinely ambiguous. An MP3 could be a music track or an audiobook chapter. The AI MediaTypeAdvisor examines tag signals — embedded ASIN (an audiobook indicator), narrator fields, duration patterns, genre tags — alongside the folder's configured category to resolve the ambiguity. If the AI cannot reach a confident classification, the item goes to the review queue for manual input.

### The Staging Area

After scanning and classification, every file lands in `.data/staging/{assetId12}/` — regardless of how confident the Engine is. Nothing is written to your organized library yet. Staging is the safe holding area where the next two phases operate. If anything goes wrong, the file is still there.

---

## Phase 2 — Retail Identification (Stage 1)

Stage 1 is the first of two enrichment phases. Its job is to find the best available match for the file among known media catalogues, and to collect the bridge IDs that make Phase 3 precise.

### The Ranked Pipeline System

Providers aren't all tried in the same way. The Engine supports three execution strategies per media type, configured in `config/pipelines.json`:

| Strategy | Behaviour | Used for |
|---|---|---|
| **Waterfall** | Providers run in rank order. First confident match wins. | Movies, TV, Comics |
| **Cascade** | All providers run independently. Claims are merged. | Books |
| **Sequential** | Providers chain: each one passes its output as input to the next. | Audiobooks, Music |

Why does this matter? Consider books. A Cascade run means both Google Books and Open Library run, and their results are merged. If Google Books has a better cover image but Open Library has a more precise ISBN, both are captured. The Priority Cascade later decides which values win — but first, both sources get to contribute.

For audiobooks, Sequential mode means the first provider's ISBN or ASIN is passed as input to the second provider, which can then do a much more targeted lookup rather than a text search. The chain sharpens precision at each step.

### What Retail Provides

Each provider returns a candidate with:

- **Cover art** — compared perceptually to the file's embedded artwork to validate the match
- **Description** — used both for display and as a match signal
- **Ratings and reader scores**
- **Bridge IDs** — the key output (see below)

### The Confidence Gate

Every retail candidate is scored against the file's metadata using `RetailMatchScoringService`. The same scorer is used both in the automated pipeline and when you manually search from the Vault detail drawer, so the numbers mean the same thing everywhere.

Field weights: title 45%, author 35%, year 10%, format 10%. Boosts apply for cross-field corroboration. Specific rules:

- A file with no author gets the author weight redistributed to title and year rather than scoring at zero
- Placeholder titles ("Unknown", "Untitled", "Track 01") return zero and route to review
- Score below 0.50: candidate discarded
- Score 0.50–0.85: candidate accepted, item flagged for review
- Score at or above 0.85: candidate auto-accepted

This gate prevents low-quality matches from quietly propagating to Wikidata. A bad Stage 1 match would produce a wrong bridge ID, which would fetch the wrong Wikidata entry, which would permanently contaminate the item's canonical metadata. The gate is strict precisely because Stage 2 trusts Stage 1's output completely.

### Bridge IDs: The Link to Wikidata

The most important output of Stage 1 is not the cover art or the description — it's the **bridge IDs**. These are the identifiers that cross-reference items between catalogues:

- ISBN-13 / ISBN-10 (books)
- ASIN (Amazon, primarily audiobooks)
- TMDB ID (films and TV)
- MusicBrainz Release ID (music)
- Apple Music ID (music, for Stage 2 Wikidata lookup via P4857)

Bridge IDs are what allow Stage 2 to find the correct Wikidata entry without relying on text search alone. "Dune" as a title search returns many results. The ISBN for a specific edition points to exactly one Wikidata item.

> **Example:** The EPUB file for *Dune* by Frank Herbert has an embedded ISBN-13. Stage 1 matches it against Google Books, confirms the title and author, collects the ISBN, and stores it as a bridge ID. Stage 2 uses that ISBN to find the Wikidata item for *Dune* (Q1375657) with near certainty.

---

## Phase 3 — Wikidata Bridge (Stage 2)

Stage 2 takes the bridge IDs from Stage 1 and uses them to fetch canonical structured data from Wikidata. This is where the file stops being a rough match and becomes a precisely identified, richly described Work with authoritative metadata.

### Why Wikidata?

Retail providers have excellent presentation data — cover art, descriptions, narrator credits. But their structured facts can be inconsistent. Publication dates might reflect a reprint rather than the original. Author name formatting varies between stores. Genre taxonomies are house-specific.

Wikidata is different. It is maintained by a global community, every fact is citable, and its structured data is stable and cross-referenced across languages. For facts — canonical title, year of first publication, genre, author, series position, franchise membership, cast — Wikidata is the most reliable automated source available.

The design principle is: **retail provides the bridge to Wikidata; Wikidata provides the canonical truth**.

### Bridge Resolution

The lookup sequence:

1. Try ISBN → find the specific Wikidata edition item, then walk up to the work item via P629 ("edition of")
2. If no ISBN, try TMDB ID, ASIN, or MusicBrainz Release ID
3. If no bridge IDs match, try title + author CirrusSearch (text fallback, with P31 instance-of type filtering to avoid false matches)

Bridge IDs make step 1 dramatically more reliable than text search. Wikidata has many items with similar titles. The ISBN is unique to one physical edition, and editions link to exactly one work.

### What Stage 2 Fetches

Once the Wikidata work QID is found, the Data Extension API fetches structured properties in a single batch call:

- **Authorship:** P50 (author), P57 (director), P58 (screenwriter), P86 (composer), P110 (illustrator)
- **Publication facts:** P577 (first publication date), P136 (genre), P921 (main subject), P1680 (subtitle)
- **Relationships:** P179 (part of series), P8345 (media franchise), P1434 (fictional universe)
- **Cast and crew:** P161 (cast member, capped at 20), P725 (voice actor), P449 (original broadcaster, TV only)
- **Cross-reference identifiers:** P212 (ISBN-13), P4947 (TMDB ID), P648 (Open Library ID)
- **Wikipedia description:** the introductory text from the associated Wikipedia article (with heading markup stripped)

For TV series and music albums, Stage 2 also discovers child entities — seasons and episodes, or tracks — in a single API call. This means when you add the first episode of a TV series, the Engine can learn the full season structure from Wikidata in one round trip, so subsequent episodes slot in without additional API calls.

### Person Reconciliation

After Stage 2, a third pass runs automatically. Every person name in the metadata — from file tags, provider data, or AI extraction — that doesn't yet have a Wikidata QID gets searched independently. Person matches are accepted at ≥0.80 confidence, with freshness checking to avoid redundant re-fetches of recently enriched people.

Musical groups are handled as single Person entries with an `IsGroup` flag. Group members resolved via P527 (has part) are stored in a junction table with optional start and end dates from Wikidata qualifiers.

---

## Phase 4 — Priority Cascade

> **Full detail:** [How the Priority Cascade Works](how-scoring-works.md)

At this point, a single item may have metadata arriving from five or six different sources: the original file, one or more retail providers, Wikidata, and potentially AI extraction. They won't all agree. The Priority Cascade resolves these conflicts — field by field, according to four tiers evaluated in strict order.

### The Four Tiers

**Tier A — User Locks**
If you manually lock a field value in the Vault, your value wins at confidence 1.0. No source overrides a user lock. This is the override of last resort — designed for the rare cases where you know something the Engine doesn't.

**Tier B — Per-Field Provider Priority**
You can configure specific providers to be the preferred authority for specific fields. For example: always prefer Apple API for cover art (professional quality, consistent), always prefer TMDB for movie poster images, always prefer MusicBrainz for track duration. Tier B lets you express domain knowledge without intervening on individual items.

**Tier C — Wikidata Authority**
For fields without a Tier B override, Wikidata claims win when present. Wikidata is the authority for facts. There is one exception: for the `author` field, if a file-embedded claim has strictly higher confidence than the best Wikidata claim, the higher-confidence claim wins. This handles pen names — a file might carry "Richard Bachman" as the author (confidence 0.95), while Wikidata would return "Stephen King" via P50 (confidence 0.75). The pen name embedded in the file should win.

**Tier D — Confidence Cascade**
When no higher tier applies, the claim with the highest confidence score wins. Confidence is set by each source based on how reliable it typically is for that field type.

### Claims Are Never Deleted

This is perhaps the most important property of the cascade: claims accumulate from all sources and are never removed, only superseded. The complete history of where every piece of data came from — and how confident each source was — is always available in the Vault's detail drawer. If a future provider produces better data, it wins on confidence. The old data is still there if you need to audit what happened.

---

## Phase 5 — Organization

Once the Priority Cascade has resolved all metadata fields, the item is ready to be organized.

### The Confidence Threshold

Items at or above **0.85 composite confidence** are automatically promoted from `.data/staging/` to their final organized location. Items below this threshold stay in staging and appear in the Vault's Action Center under "Needs Review."

The 0.85 threshold isn't arbitrary — it's the point where the Engine's track record shows false positives become rare enough that automatic promotion is safe. You can review items in the Action Center and manually confirm or correct them.

### File Organization Templates

Promoted files are placed using media-type-specific path templates:

| Media type | Template pattern |
|---|---|
| Books | `Books/{Author}/{Title} ({QID})/` |
| Audiobooks | `Audiobooks/{Author}/{Title} ({QID})/` |
| TV | `TV/{Show}/Season {N}/{SxxExx} - {EpisodeTitle}.mkv` |
| Music | `Music/{Artist}/{Album}/{TrackNumber} - {Title}.mp3` |
| Movies | `Movies/{Title} ({Year})/` |
| Comics | `Comics/{Series}/{Issue}.cbz` |

Note that `{Format}` is intentionally absent from all templates. Including the format in the path caused duplicate subfolder nesting when the same title existed in multiple formats (e.g., an EPUB and a PDF of the same book creating two separate Author/Title folders). Format is tracked at the Edition level in the data store, not on the filesystem.

### Hub Assignment

After promotion, the Engine reads the item's resolved Wikidata relationships — `series_qid` (P179), `franchise_qid` (P8345), `fictional_universe_qid` (P1434) — and assigns the Work to the appropriate Series or Universe container.

Priority order: series QID (most specific) takes precedence over franchise QID, which takes precedence over fictional universe QID. This means a book in a named series is assigned to that series rather than just the broader franchise.

Works with no Wikidata relationship properties (standalone novels, one-off films) remain unassigned and appear as individual items.

> **Example:** *Dune* (Q1375657) has P179 pointing to the Dune novel series (Q1156466). On promotion, the Engine finds or creates a Series container for the Dune novel series and assigns the Work to it. When you later add *Dune Messiah*, it resolves the same series QID and slots into the existing container — no duplicate Series is created.

### The Review Queue

Items that don't reach the confidence threshold — and items where Stage 1 found a match between 0.50 and 0.85 — appear in the Vault's Action Center. This is not a failure state; it's a deliberate design. The Engine surfaces the item with everything it knows, you can see the retail candidates it found, and you can confirm, correct, or reject the match.

Once you make a call, the item re-enters the pipeline from that point and follows the same promotion path as an auto-accepted item.

---

## How Configuration Controls the Pipeline

The pipeline's behaviour is largely driven by configuration files. Understanding which file controls what is useful when the pipeline is doing something unexpected.

### `config/pipelines.json`

Controls the ranked provider list per media type and the execution strategy (Waterfall / Cascade / Sequential). This is where you change which providers are tried for a given media type, in what order, and how their results are combined.

### `config/scoring.json`

Controls the confidence thresholds: the 0.50 discard gate, the 0.85 auto-accept threshold, and the field weights used in `RetailMatchScoringService`. Adjusting these thresholds changes how aggressively the Engine auto-accepts matches.

### `config/providers/wikidata_reconciliation.json`

The single source of truth for all Wikidata configuration:

- **`instance_of_classes`** — the P31 type allow-lists used to filter CirrusSearch text fallback results. If a media type is missing from text fallback results, this is where to look.
- **`edition_pivot_rule`** — controls how the Engine walks from an edition item to a work item when a bridge ID resolves to an edition rather than a work.
- **`reconciliation_threshold`** — minimum score for a reconciliation candidate to be accepted.
- **Language strategies** for each provider.

Previously, CirrusSearch type filters were also stored in a separate `config/cirrus-type-filters.json`. That file has been removed; `instance_of_classes` in the Wikidata config is the authoritative source for all type filtering.

### `config/providers/field_priorities.json`

Global per-field provider overrides used by Tier B of the Priority Cascade. Per-media-type overrides take precedence over these global settings and are also configured in `config/pipelines.json`.

---

## Why the Pipeline Is Designed This Way

It would be simpler to use a single source for everything, or to skip the staging phase and write directly to the organized library. The complexity exists for concrete reasons:

**Staging before organization** means the Engine can always recover. If Stage 2 fails halfway through, or you decide a match is wrong after the fact, the original file is in staging and can be re-processed cleanly without touching your library.

**Retail before Wikidata** means Stage 2 has precise bridge IDs to work with rather than relying on text search. Text search against Wikidata is ambiguous — bridge IDs are not.

**The Priority Cascade** means no single source is blindly trusted for everything. Each source contributes what it's best at: the file contributes what only it can know (your pen name preference, your series organization), retail contributes presentation data, Wikidata contributes factual authority.

**The confidence gate** means errors don't silently propagate. A borderline match goes to review rather than quietly taking root in your library and potentially linking dozens of subsequent related items to the wrong canonical entry.

**Append-only claims** mean the Engine's decisions are auditable. Every value in your library has a traceable source. If you want to understand why the author field shows "Frank Herbert" rather than "Herbert, Frank", you can look at the claim history and see exactly which source won and why.

---

## Related

- [How File Ingestion Works](how-ingestion-works.md) — file detection, fingerprinting, scanning, and staging in detail
- [How Two-Stage Enrichment Works](how-hydration-works.md) — retail identification and Wikidata bridge in detail
- [How the Priority Cascade Works](how-scoring-works.md) — claim resolution, four tiers, and confidence scoring in detail
- [Ingestion Pipeline Architecture](../architecture/ingestion-pipeline.md) — technical deep-dive
- [Scoring and Cascade Architecture](../architecture/scoring-and-cascade.md) — technical deep-dive
- [Hydration and Providers Architecture](../architecture/hydration-and-providers.md) — technical deep-dive
