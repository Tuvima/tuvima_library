---
title: "How the Library Vault Works"
summary: "See how the Vault surfaces review work, diagnostics, and management actions across your library."
audience: "user"
category: "explanation"
product_area: "vault"
tags:
  - "vault"
  - "management"
  - "review"
---

# How the Library Vault Works

The Home page and media lanes are for browsing. The **Vault** is for management: checking what the Engine thinks an item is, seeing what still needs attention, and correcting anything that looks wrong.

This page explains what appears in the Vault, what stays out of the main Vault until it is ready, and how the review tools fit together.

---

## What the Vault is for

Use the Vault when you want to:

- See which items are ready for the main library experience
- Inspect stage progress for a file that is still being processed
- Resolve uncertain retail or Wikidata matches
- Check whether artwork is present, still pending, or confirmed missing
- Re-run identification, sync metadata back to the file, or purge an item
- Browse related People, Universes, and Collections from the management side

Think of it as the control room for ingestion and metadata quality.

---

## The four tabs

### Media

The Media tab is the heart of the Vault. It shows the shared pipeline projection for each item: stage progress, readiness, artwork truth, and review state.

### People

Every author, narrator, director, composer, screenwriter, illustrator, and performer linked to your library. People are created from resolved metadata and refreshed from Wikidata over time.

### Universes

Franchise-level groupings and their linked works. This is where you inspect how the Engine connected books, films, shows, and other works into a wider world.

### Collections

Smart collections, system lists, mixes, and playlists. This tab is about presentation and curation rather than ingestion.

---

## What appears in the main Vault

Not every ingested file appears in the main Vault immediately.

An item becomes visible in the main Vault only after it passes the **Vault quality gate**:

- It has a real, non-placeholder title
- It has a resolved media type
- Its artwork state has settled, either `present` or explicitly `missing`

If one of those conditions is not true yet, the item stays available in **Activity**, **Review**, and the **Action Center**, but it does not surface in the main Vault list yet.

This is why a newly ingested file may be "known to the system" before it appears in the main library-facing views.

---

## How the Media tab tells the story

The Media tab now uses the same three pipeline stages everywhere:

| Stage | What it means |
|---|---|
| **Retail** | Retail providers are being searched and scored for the best practical match. |
| **Wikidata** | Bridge identifiers from Retail are being used to resolve a canonical Wikidata identity. |
| **Enrichment** | The item is past identity resolution and is being deepened with follow-up metadata, images, people, and relationships. |

Alongside the stages, the UI uses a plain-English readiness label:

- **Pending artwork**: identity may be far enough along, but artwork has not settled yet
- **Needs review**: the pipeline found an uncertain result and wants a human choice
- **Ready**: the item passed the Vault quality gate and is visible in the main Vault

One special case is **QID Not Found**. That means Retail succeeded but Wikidata did not produce a canonical QID. The item stays marked at the Wikidata stage, but it can still be ready for the main Vault if its title, media type, and artwork state are settled.

---

## Statuses and readiness

The best mental model is:

- **Ready / Verified**: the item is usable and visible in the main Vault
- **Needs Review**: the Engine found something uncertain and wants confirmation
- **Pending**: the pipeline is still actively working
- **Quarantined**: a file or processing problem blocked normal handling

Some older APIs and compatibility views may still expose values such as `Provisional`. Treat those as compatibility labels rather than as the primary Vault mental model. The main UI story is stage progress plus readiness.

---

## The detail drawer

Clicking an item opens the detail drawer. It stays on the same page and gives you the full audit trail for that one file.

Typical sections:

- **Header**: title, creator, status, readiness, and artwork
- **Sync**: whether writeback is enabled and when it last ran
- **Enrichment**: descriptions, ratings, subjects, bridge IDs, and other resolved metadata
- **Pipeline**: the three stages, what each one did, and what is blocked
- **File**: path, size, format, fingerprint, and source folder
- **Claims**: every metadata claim the Engine saw, with source and confidence

The drawer is where you answer the question, "Why does the Engine believe this item is what it says it is?"

---

## Resolving the Retail stage

When the Retail stage needs help, the drawer shows a resolution panel with:

- The current best candidate, if one exists
- Other ranked candidates from providers
- Manual retail search
- **Add Provisional**, which starts from file or user-supplied metadata instead of a provider match

`Add Provisional` is best thought of as a local metadata correction flow. It gives the Engine better input and preserves your intent, but it does not force the item into the main Vault before the quality gate is satisfied.

---

## Resolving the Wikidata stage

When the Wikidata stage needs help, you may see:

- A ranked list of QID candidates
- Manual Wikidata search
- Direct QID entry if you already know the right entity
- A "skip Wikidata" path when the work is real but no good QID is available

Skipping Wikidata does **not** mean the item disappears. If the item otherwise has a trustworthy title, resolved media type, and settled artwork result, it can still remain visible in the main Vault as a QID-missing outcome.

---

## Batch operations

You can work through many items at once:

- `Shift+click` selects a range
- `Ctrl+click` adds or removes individual items
- Group actions let you re-run sync or delete multiple items together

Batch work is most useful for large review sessions after a bulk import.

---

## People and Universes in practice

The People and Universes tabs are downstream of the same identity work:

- **People** become richer as Wikidata and later enrichment add more roles, headshots, and links
- **Universes** become more trustworthy as more works resolve to the right series, franchise, and relationship graph

Because of that, a missing or incorrect match in Media often explains a strange result in People or Universes.

---

## The short version

The Vault is no longer "every file immediately." It is the management surface built on a shared projection of:

- where the item is in the pipeline
- whether the main Vault should show it yet
- whether artwork is really there, still pending, or confirmed missing
- what still needs a human decision

That makes the Vault calmer and more trustworthy: items appear slightly later, but when they do appear, the UI is telling one consistent story.

## Related

- [How the Entire Pipeline Works](how-the-pipeline-works.md)
- [How Two-Stage Enrichment Works](how-hydration-works.md)
- [How to Resolve Items That Need Review](../guides/resolving-reviews.md)
- [How File Ingestion Works](how-ingestion-works.md)
