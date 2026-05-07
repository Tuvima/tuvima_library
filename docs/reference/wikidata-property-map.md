---
title: "Wikidata Property Map"
summary: "Reference table of 40+ Wikidata properties used for metadata enrichment, with P-codes, claim keys, and confidence levels."
audience: "developer"
category: "reference"
product_area: "providers"
tags:
  - "wikidata"
  - "properties"
  - "stage-3"
---

# Wikidata Property Map

> **Stage 3 Planning Reference:** These property definitions document the Wikidata P-codes used across the enrichment pipeline. The Lore & Narrative properties (P840, P674, P921, P1434, P144, P4584) are reserved for the Stage 3 Universe Enrichment feature. Since the project uses the Tuvima.Wikidata library for API access, these properties are consumed through the library's sub-services — the config-level property map has been moved to this reference doc until Stage 3 design determines the actual integration shape.

---

## Core Identity Properties

| P-code | Claim Key | Scope | Confidence | Notes |
|--------|-----------|-------|------------|-------|
| P31 | `instance_of` | Work | 0.9 | Entity-valued |
| P1476 | `title` | Work | 0.9 | Monolingual text |
| P179 | `series` | Work | 0.9 | Entity-valued |
| P1545 | `series_position` | Work | 0.9 | Numeric portion extracted |
| P8345 | `franchise` | Work | 0.9 | Entity-valued |
| P155 | `preceded_by` | Work | 0.8 | Entity-valued |
| P156 | `followed_by` | Work | 0.8 | Entity-valued |
| P577 | `year` | Work | 0.9 | Year extracted from ISO date |

---

## People Properties — Work Scope

| P-code | Claim Key | Scope | Confidence | Notes |
|--------|-----------|-------|------------|-------|
| P50 | `author` | Work | 0.9 | Entity-valued |
| P110 | `illustrator` | Work | 0.9 | Entity-valued |
| P57 | `director` | Work | 0.9 | Entity-valued |
| P161 | `cast_member` | Work | 0.9 | Entity-valued, capped at 20 |
| P987 | `narrator` | Work | 0.9 | Entity-valued |
| P725 | `voice_actor` | Work | 0.9 | Entity-valued |
| P58 | `screenwriter` | Work | 0.9 | Entity-valued |
| P86 | `composer` | Work | 0.9 | Entity-valued |

---

## People Properties — Person Scope

| P-code | Claim Key | Scope | Confidence | Notes |
|--------|-----------|-------|------------|-------|
| P800 | `notable_work` | Person | 0.85 | Entity-valued |
| P18 | `headshot_url` | Person | 0.9 | Commons URL transform |
| P106 | `occupation` | Person | 0.85 | Entity-valued |
| P742 | `pseudonym` | Person | 0.85 | Entity-valued |
| P1773 | `attributed_to` | Person | 0.85 | Entity-valued |

---

## Lore & Narrative Properties

These properties are reserved for the **Stage 3 Universe Enrichment** feature. They are not consumed by the current pipeline.

| P-code | Claim Key | Scope | Confidence | Notes |
|--------|-----------|-------|------------|-------|
| P840 | `narrative_location` | Work | 0.8 | Entity-valued |
| P674 | `characters` | Work | 0.8 | Entity-valued |
| P921 | `main_subject` | Work | 0.8 | Entity-valued |
| P1434 | `fictional_universe` | Work | 0.8 | Entity-valued |
| P144 | `based_on` | Work | 0.8 | Entity-valued |
| P4584 | `first_appearance` | Work | 0.8 | Entity-valued |
| P451 | `partner` | Work | 0.9 | Entity-valued |
| P69 | `educated_at` | Work | 0.9 | Entity-valued |
| P945 | `allegiance` | Work | 0.9 | Entity-valued |
| P39 | `position_held` | Work | 0.9 | Entity-valued |
| P607 | `conflict` | Work | 0.9 | Entity-valued |

---

## Bridge Identifiers

Bridge identifiers are external IDs used to cross-reference Wikidata entities with retail and specialist providers. They enable precise QID resolution in Stage 2 of the hydration pipeline.

### Books

| P-code | Identifier | Notes |
|--------|-----------|-------|
| P3861 | Apple Books ID | |
| P212 | ISBN | |
| P1566 | ASIN | |
| P2969 | Goodreads ID | |
| P244 | Library of Congress ID | |

### Movies & TV

| P-code | Identifier | Notes |
|--------|-----------|-------|
| P4947 | TMDB ID | Used by Fanart.tv image enrichment |
| P345 | IMDb ID | |
| P9385 | JustWatch ID | |
| P1712 | Metacritic ID | |
| P6127 | Letterboxd ID | |
| P2638 | TV.com ID | |

### Comics & Anime

| P-code | Identifier | Notes |
|--------|-----------|-------|
| P3589 | GCD Series ID | Grand Comics Database |
| P11308 | GCD Issue ID | Grand Comics Database |
| P5905 | ComicVine ID | |
| P4084 | MAL Anime ID | MyAnimeList |
| P4087 | MAL Manga ID | MyAnimeList |

### Music & Audio

| P-code | Identifier | Notes |
|--------|-----------|-------|
| P434 | MusicBrainz ID | Used by Fanart.tv image enrichment |
| P1902 | Spotify ID | |
| P1953 | Discogs ID | |
| P3398 | Audible ID | |

---

## Social Pivot Properties — Person Scope

Used to populate actionable social links on Person entities (see Universe Graph & Chronicle Engine, §3.9).

| P-code | Claim Key | Platform |
|--------|-----------|----------|
| P2003 | `instagram` | Instagram |
| P2002 | `twitter` | X / Twitter |
| P7085 | `tiktok` | TikTok |
| P4033 | `mastodon` | Mastodon |
| P856 | `website` | Official website |

---

## Notes on Confidence Levels

Confidence values indicate the strength of trust assigned to a claim when it enters the Priority Cascade:

| Value | Meaning |
|-------|---------|
| 0.9 | High confidence — Wikidata is the primary authority for this field |
| 0.85 | Good confidence — used for person-scope properties and identifiers |
| 0.8 | Moderate confidence — lore/narrative properties where Wikidata coverage varies |

For the `author` field specifically, a non-Wikidata claim with strictly higher confidence than the best Wikidata P50 claim wins in Tier C of the cascade (to honour deliberately reduced P50 author confidence of 0.75 and preserve embedded pen names at 0.95). All other fields follow standard Tier C Wikidata authority.

See [`docs/architecture/scoring-and-cascade.md`](../architecture/scoring-and-cascade.md) for the full Priority Cascade rules.

