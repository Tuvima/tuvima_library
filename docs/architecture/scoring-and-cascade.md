---
title: "Priority Cascade Engine"
summary: "Deep technical documentation for metadata claims, weights, thresholds, and conflict resolution."
audience: "developer"
category: "architecture"
product_area: "scoring"
tags:
  - "scoring"
  - "metadata"
  - "conflicts"
---

# Priority Cascade Engine

## Purpose

When multiple sources disagree about a metadata field â€” title, author, year, cover art â€” the Priority Cascade Engine resolves the dispute and produces a single canonical value for each field. Every piece of metadata is modeled as a **Claim**: a triple of (source, value, confidence). Claims accumulate from all sources. The cascade picks a winner per field.

---

## Claim Sources and Base Weights

| Source | Base confidence | Notes |
|---|---|---|
| File internal metadata (OPF, ID3) | 0.9 | High trust â€” embedded at creation time |
| Filename | 0.5 | Medium trust â€” often approximate or user-modified |
| External providers | Configurable per field | See per-field trust weights below |
| User lock | 1.0 | Absolute â€” see Tier A |

External providers declare per-field trust weights in their provider config files (`config/providers/`). These weights reflect how reliable that provider is for a specific kind of data â€” Wikidata carries franchise identifiers at weight 1.0, Apple API carries cover art at 0.85, and so on.

---

## Priority Cascade Tiers

Tiers are evaluated in order. The first tier that can resolve a field wins; lower tiers are not consulted for that field.

### Tier A â€” User Locks

User-locked claims always win, regardless of any provider or scoring result. A user-locked claim carries confidence 1.0 and is never overridden on any future re-score. This guarantee is absolute.

### Tier B â€” Per-Field Provider Priority

Some fields benefit from a specific provider rather than the default Wikidata-always-wins rule. When a field has an override in `config/field_priorities.json`, the cascade walks the provider priority list and returns the first provider that has a claim for that field. Tier C is skipped entirely for this field.

Example overrides from `config/field_priorities.json`:

| Field | Priority order | Reason |
|---|---|---|
| `description` | wikipedia, apple_api, wikidata_reconciliation | Rich Wikipedia summaries preferred over Wikidata one-liners |
| `cover` | apple_api, tmdb, wikidata_reconciliation | Retail providers have high-resolution commercial art |
| `rating` | apple_api, tmdb | Wikidata does not carry ratings |
| `biography` | wikipedia, wikidata_reconciliation | Rich Wikipedia bios for persons |

Fields not listed in the config default to Tier C (Wikidata authority).

### Tier C â€” Wikidata Authority

For any field without a Tier B override, Wikidata claims win unconditionally when present. Wikidata is the sole identity authority â€” every media item is identified by its Wikidata Q-identifier.

### Tier D â€” Confidence Cascade

When no Tier A, B, or C claim exists for a field, the highest-confidence claim across all remaining sources wins.

---

## Field Count Scaling

Files with very few metadata fields receive a confidence penalty to prevent inflated scores from near-empty files:

```
overallConfidence *= Math.Min(1.0, fieldCount / 3.0)
```

A file with only one field scores at approximately 1/3 of its raw confidence. A file with three or more fields is unaffected (multiplier = 1.0). This ensures corrupt or near-empty files are routed to staging for review rather than being auto-promoted.

---

## Retail Match Scoring

Missing metadata fields score **0.0** (not the neutral 0.5 they previously received). An absent value is evidence of a poor match, not absence of evidence.

**Placeholder title detection:** Titles matching known placeholder patterns — "Unknown", "Untitled", and track-number patterns such as "Track 01" — are scored 0.0 and routed directly to the review queue. These indicate the file has no real title metadata and cannot be auto-matched.

**Retail score thresholds** (configured in `config/hydration.json`):

| Key | Value | Meaning |
|---|---|---|
| `retail_auto_accept_threshold` | 0.90 | Match accepted automatically |
| `retail_ambiguous_threshold` | 0.65 | Match flagged for review |

Scores below `retail_ambiguous_threshold` are discarded; the pipeline proceeds to the next ranked provider.

Additional contradiction gates apply before auto-accept. Weak creator agreement caps a candidate to review, grouped TV auto-accept requires exact show/season/episode agreement, grouped music auto-accept requires track-number or duration corroboration, and cover similarity cannot rescue a weak text match by itself.

## Wikidata Author Validation

When the Stage 2 Wikidata candidate is scored, the author from the file's embedded metadata is compared against the candidate's P50 (author) property:

| Condition | Score adjustment |
|---|---|
| Author similarity < 0.3 (clear mismatch) | −25 penalty |
| Candidate has no author properties (P50 absent) | −15 penalty |

These penalties apply on top of the base reconciliation score before the `wikidata_review_threshold` and `wikidata_auto_accept` gates are evaluated.

**Wikidata score thresholds** (configured in `config/scoring.json`):

| Key | Value | Meaning |
|---|---|---|
| `wikidata_review_threshold` | 55 | Below this score: item goes to review queue |
| `wikidata_auto_accept` | 95 | At or above this score and `match: true`: QID accepted automatically |

---

## Conflicted Fields

When two claims for the same field are too close in confidence to pick a clear winner, the field is marked **Conflicted** and surfaced to the user for manual resolution. The conflict threshold and epsilon are configured in `config/scoring.json`.

---

## Claim History

All claims are stored append-only. No claim is ever deleted or overwritten â€” only superseded by higher-priority claims. The full provenance trail for every field is always available.

---

## Auto-Link and Promotion Gates

The auto-link threshold (`auto_link_threshold`) in `config/scoring.json` governs when a scored file is automatically promoted from staging to the organised library:

- Files with `overallConfidence >= 0.85` or any user-locked claim are promoted automatically
- Files below the gate go to `.staging/low-confidence/` or `.staging/unidentifiable/` depending on their score

---

## Configuration Reference

All scoring parameters live in `config/scoring.json`:

| Key | Default | Purpose |
|---|---|---|
| `auto_link_threshold` | 0.85 | Confidence gate for automatic staging promotion |
| `conflict_threshold` | 0.60 | Below this, a field is not auto-resolved |
| `conflict_epsilon` | 0.05 | Maximum difference for two claims to be considered tied |
| `stale_claim_decay_days` | 90 | Claims older than this begin to decay |
| `stale_claim_decay_factor` | 0.8 | Multiplier applied to confidence of stale claims |
| `retail_auto_accept_threshold` | 0.90 | Retail match score threshold for automatic acceptance |
| `retail_ambiguous_threshold` | 0.65 | Retail match score threshold below which a match is discarded |
| `wikidata_review_threshold` | 55 | Wikidata reconciliation score below which item goes to review |
| `wikidata_auto_accept` | 95 | Wikidata reconciliation score at which QID is auto-accepted |

Per-field provider priority overrides live in `config/field_priorities.json`.

---

## Unified Retail Match Scoring

`RetailMatchScoringService` is the **single scoring implementation** used by both the automated pipeline (Stage 1 retail confidence gate) and manual search (shared media editor search). This ensures that search results and pipeline decisions use identical scoring logic.

### Field Weights

| Field | Default Weight | Notes |
|---|---|---|
| Title | 0.45 | Token-set-ratio fuzzy match |
| Author | 0.35 | Multi-author splitting with proportional scoring |
| Year | 0.10 | Exact = 1.0, ±1 year = 0.8, otherwise 0.3 |
| Format | 0.10 | Always 1.0 (strategies are media-type-scoped) |

Weights are configurable in `config/hydration.json` → `fuzzy_match_weights`.

### Multi-Author Matching

When the full-string author comparison scores below 0.70, both file and candidate authors are split on common separators (`&`, `and`, `,`) and each name is matched independently. Score = matched / max(file count, candidate count). For example, "Neil Gaiman & Terry Pratchett" vs "Terry Pratchett" scores 0.5 (1 of 2 matched).

### Cross-Field Boost Signals

Additive boost (positive or negative) from cross-referencing file metadata against candidate extended metadata:

| Signal | Boost | Condition |
|---|---|---|
| Narrator in description | +0.10 | Audiobooks only |
| Author in description | +0.08 | Books/Audiobooks |
| Series name in description | +0.08 | All media types |
| Publisher matches | +0.05 | Books only, fuzzy ≥ 0.85 |
| Page count within 10% | +0.05 | Books only |
| Duration within 15% | +0.05 | Audiobooks only |
| Duration wildly different (>50%) | −0.10 | Audiobooks only |
| Genre overlap | +0.05 | All media types |
| Language matches | +0.05 | All media types |
| Language mismatch | −0.10 | All media types |
| Cover art strong match (>0.8) | +0.10 | When cover art hashing is available |
| Cover art moderate match (>0.6) | +0.05 | When cover art hashing is available |

### Placeholder Title Rejection

Files with placeholder titles ("Unknown", "Untitled", "Untitled Book", "New Recording", "Track XX") receive a zero-score immediately and route to the review queue.

### Pipeline Confidence Gate

After Stage 1 providers return results, `RetailMatchScoringService` scores each candidate:
- **CompositeScore >= 0.90** -> auto-accepted, proceeds to Stage 2
- **0.65 <= CompositeScore < 0.90** -> accepted with review flag (appears in Review Queue)
- **CompositeScore < 0.65** -> rejected, next provider tried

Candidate evidence is persisted with richer audit detail, including field scores, threshold path, rejection reasons, and whether the candidate came from grouped processing or single-item fallback.

---

## Wikidata Candidate Ranking

`ReconciliationAdapter.FilterByMediaTypeAsync` applies multi-author matching against P50/P175 properties. Penalties:

| Condition | Penalty | Rationale |
|---|---|---|
| bestAuthorMatch < 0.3 | −35 | Strong author mismatch — likely wrong work |
| No P50/P175 properties at all | −40 | Entity has no author/performer data — highly suspect |

Score blending: 85% composite (type-aware scoring) / 15% original Wikidata API score. This ensures type filtering and author matching have strong influence over raw label-match scores.

---

## Pipeline Enforcement — No Retail, No Wikidata

Stage 2 (Wikidata) requires bridge IDs from Stage 1 (retail). If Stage 1 produces no match:
- The text-only Wikidata fallback is **removed** — no automatic text reconciliation bypass
- The item routes directly to the review queue with `AuthorityMatchFailed`

`ResolveBridgeAsync` sentinel guard: when only sentinel keys (`_title`, `_author`) are provided with no real bridge IDs, the text fallback is blocked and the item returns `NotFound`. Real bridge IDs that were attempted and failed still allow text reconciliation as a last resort.

---

## AI and the Cascade

AI features in `MediaEngine.AI` improve the quality of inputs to the cascade â€” they do not replace it. The cascade determines all final canonical values.

| AI feature | Role in scoring |
|---|---|
| SmartLabeler | Cleans filenames before they are parsed into claims, producing better Tier D candidates |
| MediaTypeAdvisor | Classifies ambiguous file formats, emitting a high-confidence `media_type` claim |
| QidDisambiguator | Picks the best Wikidata candidate when the Reconciliation API returns multiple matches, accelerating Tier C resolution |
| BatchManifestBuilder | Reduces retail API calls during bulk ingestion, but does not change how claims are weighted or selected |

Wikidata remains the authority for all canonical data. AI accelerates and improves the matching process that feeds the cascade; the cascade itself is unchanged.

## Lineage-Aware Claim Routing (Phase 3)

Library Works form a hierarchy: a TV episode lives under a season under a show; a music track lives under an album; a comic issue lives under a series. Until Phase 3, every metadata claim a worker produced — *including* facts about the parent (the show's name, the album's release year, the series' description) — was written against the file on disk. The result was that show, season, and episode looked like the same row in the data store, and the Engine couldn't tell them apart.

Phase 3 introduces a small routing layer that decides which Work in the hierarchy each claim belongs to.

### Components

| Component | Layer | Role |
|---|---|---|
| `WorkLineage` (record) | Domain.Contracts | Walked chain `asset → edition → work → parent → root parent` returned by `IWorkRepository.GetLineageByAssetAsync`. `TargetForParentScope = RootParentWorkId` (TV episodes resolve up to the SHOW, not the season); `TargetForSelfScope = WorkId`. |
| `ClaimScope` (enum) | Domain.Constants | `Self` or `Parent`. New claim keys default to `Self`. |
| `ClaimScopeCatalog` | Domain.Constants | Single source of truth mapping `(claim_key, media_type)` → `ClaimScope`. Container fields (`album`, `show_name`, `series`, `franchise`) and container bridge IDs (`apple_music_collection_id`, `tvdb_id`, etc.) declare `Parent`; per-media-type overrides handle context-sensitive cases (e.g. `year` is `Parent` for music but `Self` for movies; `director` is `Self` for TV episodes but `Self` for movies). |
| `WorkClaimRouter` | Storage.Services | Stateless splitter for bridge ID dictionaries and `MetadataClaim` lists. Used by Phase 3a/3b for `works.external_identifiers` writes. |
| `ScoringHelper.PersistAndScoreWithLineageAsync` | Providers.Services | Phase 3c entry point. Runs the existing per-asset persist+score path unchanged, then mirrors `Parent`-scoped claims into the parent Work's `metadata_claims` and `canonical_values` as a second pass. |

### Three-phase rollout

| Phase | Scope | Status |
|---|---|---|
| **3a** | `RetailMatchWorker` writes provider bridge IDs to `works.external_identifiers` JSON via the router (track-level IDs on the asset's Work, container IDs on the parent). | Shipped |
| **3b** | `WikidataBridgeWorker` routes resolved QIDs and container bridge IDs the same way; child manifests trigger `CatalogUpsertService.UpsertChildrenAsync` against the parent Work. | Shipped |
| **3c** | All four call sites in `RetailMatchWorker`, `WikidataBridgeWorker`, and `DescriptionIntelligenceBatchService` switch to `PersistAndScoreWithLineageAsync`. Display claims (title, year, description, cover, genre, cast) now mirror onto the parent Work's `canonical_values` in addition to the asset's. | Shipped |

### Dual write during transition

Phase 3c is intentionally **dual-write**: the asset's `metadata_claims` and `canonical_values` rows still receive the full picture (so existing media library library item CTEs and Review Queue queries don't regress), and the parent Work additionally receives an authoritative copy of the `Parent`-scoped fields. Phase 4 will teach readers (library item CTEs, collection rule evaluator, detail drawer queries) to consult the parent Work directly. Phase 5 will retire the asset-side mirror once readers are ported. Phase 6 will run a one-shot backfill that walks every existing asset, computes its lineage, and re-routes historical claims to the right Work rows.

The parent-side mirror is best-effort: failures are logged at warning level and never break the asset-side write. Movies and single-volume books (where `TargetForParentScope == TargetForSelfScope`) skip the parent pass entirely — the dual write collapses to a single write.

### Adding new claim keys

When a provider starts emitting a new claim key, decide its scope:

1. **Self** (the default): no action needed. The key will be written against the asset.
2. **Parent**: add an entry to `ClaimScopeCatalog.DefaultMap` if the scope is the same across all media types, or to `ClaimScopeCatalog.Overrides[mediaType]` if it depends on context.

The companion `_qid` suffix is handled automatically — `genre_qid` inherits the scope of `genre`. Tests live in `tests/MediaEngine.Domain.Tests/ClaimScopeCatalogTests.cs` and `tests/MediaEngine.Providers.Tests/ScoringHelperLineageTests.cs`.

## Related

- [How the Priority Cascade Works](../explanation/how-scoring-works.md)
- [Database Schema Reference](../reference/database-schema.md)
- [How to Resolve Items That Need Review](../guides/resolving-reviews.md)

