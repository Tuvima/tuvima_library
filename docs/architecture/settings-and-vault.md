---
title: "Settings Architecture and Library Vault"
summary: "Deep technical documentation for settings surfaces, Vault flows, and operational management screens."
audience: "developer"
category: "architecture"
product_area: "vault"
tags:
  - "settings"
  - "vault"
  - "operations"
---

# Settings Architecture & Library Vault

This document covers two related product surfaces:

- **Settings**, where users control how the Engine behaves
- **Vault**, where users inspect and manage the results of ingestion and enrichment

The current Vault architecture is projection-driven. Main Vault visibility, stage progress, readiness, and artwork truth all come from the same shared storage projection rather than from separate UI heuristics.

---

## Settings design principles

Every setting falls into one of four categories:

1. **Config-only**: dangerous or low-frequency settings that belong in JSON files
2. **First-run settings**: captured in the setup flow, but editable later if needed
3. **Actively managed settings**: changed often enough to deserve a GUI
4. **Task-oriented grouping**: settings are grouped by what the user is trying to do, not by internal subsystem

---

## First-run wizard

The first-run wizard exists to capture the minimum viable setup:

1. Identity and locale
2. Library folders and library root
3. Provider credentials where needed
4. AI model choices
5. Final review and launch

It should stay focused on "what is required to become operational," not on every advanced tuning option.

---

## Settings information architecture

The long-term settings split is:

| Area | Purpose |
|---|---|
| **Library** | Source folders, library roots, media types, intake behavior |
| **Providers** | API keys, provider enablement, language/region preferences |
| **Intelligence** | AI features, enrichment cadence, quality-related toggles |
| **Server** | health, security, maintenance, logs, diagnostics |

Detailed runtime behavior still lives primarily in `config/`, but the GUI should expose the actively managed subset.

---

## Vault purpose

The Vault is the management surface for ingestion and metadata quality.

It answers questions like:

- What is ready for the main library experience?
- What is still blocked?
- Which stage is this item in?
- Does this item really have artwork, or is it still pending?
- Does this item need review, or is it simply still processing?

The Vault is not just "everything the Engine has ever seen." It is the operational view of a shared projection over:

- library item data
- identity job state
- review queue state
- canonical artwork flags

---

## Main Vault visibility

Main Vault visibility is controlled by the **Vault quality gate**.

An item becomes visible in the main Vault only after it has:

- a non-placeholder title
- a resolved media type
- a settled artwork outcome

Settled artwork means:

- `present`
- or `missing` after the artwork pass explicitly settled it

If an item does not pass that gate yet, it remains visible in **Activity**, **Review**, and the **Action Center**, but not in the main Vault media list.

This is a deliberate product choice: the Vault should tell one calm, honest story rather than showing half-finished items too early.

---

## Vault tabs

The Vault has four top-level tabs:

| Tab | Purpose |
|---|---|
| **Media** | Item-level management, review, pipeline inspection, and fixes |
| **People** | People records derived from resolved media metadata |
| **Universes** | Franchise and world-level groupings and graph health |
| **Collections** | Smart collections, system lists, mixes, and playlists |

The Media tab is where ingestion and matching quality are primarily managed.

---

## Media tab architecture

Each Media row is a projection of one item that combines:

- current title and creator
- current status
- current pipeline step
- current Vault visibility
- current artwork state and source
- whether the item is ready for the main Vault

### Standard stages

The UI now uses the same three stages everywhere:

| Stage | Meaning |
|---|---|
| **Retail** | Provider matching and bridge-ID collection |
| **Wikidata** | Canonical identity resolution |
| **Enrichment** | Follow-up metadata, people, images, and relationships |

This replaces older mixed models where list views, cards, and detail drawers could disagree.

### Readiness labels

The primary user-facing summary is a readiness label:

- **Pending artwork**
- **Needs review**
- **Ready**

These labels are derived from the same projection as the stage dots and overview counts.

### Compatibility statuses

Some older API payloads or compatibility views still expose status values such as `Provisional`. Those values remain for compatibility, but the preferred product mental model is:

- stage progress
- readiness
- artwork truth

---

## Media list design

The Media list should prioritize quick triage.

Recommended columns:

| Column | Purpose |
|---|---|
| **Thumbnail** | Cover art when present, placeholder otherwise |
| **Item** | Title and primary creator |
| **Universe / Context** | Grouping context when available |
| **Pipeline** | Retail, Wikidata, Enrichment stage dots |
| **Status / Readiness** | Human-readable state and actionability |

Problem items may show an additional reason line. Healthy items should stay compact.

---

## Vault overview

The Vault overview now depends on the same projection as list and detail views.

In addition to older health totals, it should surface:

- `hidden_by_quality_gate`
- `art_pending`
- `retail_needs_review`
- `qid_no_match`
- `completed_with_art`

These counts are meant to answer, at a glance:

- how much work is blocked by quality gating
- how much is waiting on art
- how much is truly ready

---

## Detail drawer architecture

The detail drawer is the primary inspection surface for a single item.

Recommended sections:

- **Header**: title, creator, status, readiness, artwork
- **Sync**: writeback state and sync history
- **Enrichment**: descriptions, bridge IDs, ratings, and resolved metadata
- **Pipeline**: the three stages and what each one did
- **File**: path, size, format, fingerprint, source folder
- **Claims**: full provenance and conflict history

The drawer should answer both:

- "What does the Engine believe?"
- "Why does it believe that?"

---

## Inline resolution flows

### Retail

Retail resolution supports:

- current best candidate
- alternate candidates
- manual retail search
- local metadata correction through **Add Provisional**

`Add Provisional` should be treated as a local metadata override flow. It improves the input to future matching, but it does not bypass the Vault quality gate.

### Wikidata

Wikidata resolution supports:

- ranked QID candidates
- manual search
- direct QID entry
- accept-without-QID flow

If Retail succeeded but no good Wikidata QID exists, the product should show that explicitly rather than silently pretending the item is fully resolved.

---

## Artwork architecture in the Vault

Artwork is now represented explicitly rather than inferred from URLs alone.

The projection exposes:

- `artworkState`
- `artworkSource`
- `artworkSettledAt`

Canonical storage persists artwork truth using keys such as:

- `cover_state`
- `cover_source`
- `hero_state`
- `artwork_settled_at`

Product implications:

- `CoverUrl` should only be returned when cover art is actually present
- placeholders should be used when art is missing or still pending
- readiness should wait for artwork settlement, not just for a download attempt

---

## People, Universes, and Collections

These tabs are downstream of the same identity work:

- **People** get richer as more works resolve correctly
- **Universes** become more accurate as canonical identity improves
- **Collections** sit at the presentation layer, but depend on trustworthy underlying media data

Because of that, ingestion quality work in Media directly improves the quality of the other tabs.

---

## Architecture implications

The important architectural decisions are:

- one shared projection should drive list, detail, and overview surfaces
- main Vault visibility should be a quality-gate decision, not a side effect of filesystem staging
- artwork truth should be explicit and durable
- QID-missing outcomes should remain visible as first-class states when the item is otherwise good enough

That combination makes the Vault calmer, more trustworthy, and much easier to reason about.

## Related

- [Ingestion Pipeline](ingestion-pipeline.md)
- [Hydration Pipeline, Provider Architecture and Enrichment Strategy](hydration-and-providers.md)
- [Scoring and Cascade Architecture](scoring-and-cascade.md)
- [How the Library Vault Works](../explanation/how-the-vault-works.md)
