# Priority Cascade Engine

## Purpose

When multiple sources disagree about a metadata field — title, author, year, cover art — the Priority Cascade Engine resolves the dispute and produces a single canonical value for each field. Every piece of metadata is modeled as a **Claim**: a triple of (source, value, confidence). Claims accumulate from all sources. The cascade picks a winner per field.

---

## Claim Sources and Base Weights

| Source | Base confidence | Notes |
|---|---|---|
| File internal metadata (OPF, ID3) | 0.9 | High trust — embedded at creation time |
| Filename | 0.5 | Medium trust — often approximate or user-modified |
| External providers | Configurable per field | See per-field trust weights below |
| User lock | 1.0 | Absolute — see Tier A |

External providers declare per-field trust weights in their provider config files (`config/providers/`). These weights reflect how reliable that provider is for a specific kind of data — Wikidata carries franchise identifiers at weight 1.0, Apple API carries cover art at 0.85, and so on.

---

## Priority Cascade Tiers

Tiers are evaluated in order. The first tier that can resolve a field wins; lower tiers are not consulted for that field.

### Tier A — User Locks

User-locked claims always win, regardless of any provider or scoring result. A user-locked claim carries confidence 1.0 and is never overridden on any future re-score. This guarantee is absolute.

### Tier B — Per-Field Provider Priority

Some fields benefit from a specific provider rather than the default Wikidata-always-wins rule. When a field has an override in `config/field_priorities.json`, the cascade walks the provider priority list and returns the first provider that has a claim for that field. Tier C is skipped entirely for this field.

Example overrides from `config/field_priorities.json`:

| Field | Priority order | Reason |
|---|---|---|
| `description` | wikipedia, apple_api, wikidata_reconciliation | Rich Wikipedia summaries preferred over Wikidata one-liners |
| `cover` | apple_api, tmdb, wikidata_reconciliation | Retail providers have high-resolution commercial art |
| `rating` | apple_api, tmdb | Wikidata does not carry ratings |
| `biography` | wikipedia, wikidata_reconciliation | Rich Wikipedia bios for persons |

Fields not listed in the config default to Tier C (Wikidata authority).

### Tier C — Wikidata Authority

For any field without a Tier B override, Wikidata claims win unconditionally when present. Wikidata is the sole identity authority — every media item is identified by its Wikidata Q-identifier.

### Tier D — Confidence Cascade

When no Tier A, B, or C claim exists for a field, the highest-confidence claim across all remaining sources wins.

---

## Field Count Scaling

Files with very few metadata fields receive a confidence penalty to prevent inflated scores from near-empty files:

```
overallConfidence *= Math.Min(1.0, fieldCount / 3.0)
```

A file with only one field scores at approximately 1/3 of its raw confidence. A file with three or more fields is unaffected (multiplier = 1.0). This ensures corrupt or near-empty files are routed to staging for review rather than being auto-promoted.

---

## Conflicted Fields

When two claims for the same field are too close in confidence to pick a clear winner, the field is marked **Conflicted** and surfaced to the user for manual resolution. The conflict threshold and epsilon are configured in `config/scoring.json`.

---

## Claim History

All claims are stored append-only. No claim is ever deleted or overwritten — only superseded by higher-priority claims. The full provenance trail for every field is always available.

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

Per-field provider priority overrides live in `config/field_priorities.json`.

---

## AI and the Cascade

AI features in `MediaEngine.AI` improve the quality of inputs to the cascade — they do not replace it. The cascade determines all final canonical values.

| AI feature | Role in scoring |
|---|---|
| SmartLabeler | Cleans filenames before they are parsed into claims, producing better Tier D candidates |
| MediaTypeAdvisor | Classifies ambiguous file formats, emitting a high-confidence `media_type` claim |
| QidDisambiguator | Picks the best Wikidata candidate when the Reconciliation API returns multiple matches, accelerating Tier C resolution |
| BatchManifestBuilder | Reduces retail API calls during bulk ingestion, but does not change how claims are weighted or selected |

Wikidata remains the authority for all canonical data. AI accelerates and improves the matching process that feeds the cascade; the cascade itself is unchanged.
